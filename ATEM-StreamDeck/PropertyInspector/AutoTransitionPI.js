// Auto Transition Action Property Inspector using easypi-v2

var atemInfoCache = {};

// Initialize PropertyInspector 
function initPropertyInspector() {
    console.log('Auto Transition PI initialized');
    
    // Set up IP address change listener to request ATEM info
    const atemIPElement = document.getElementById('atemIPAddress');
    if (atemIPElement) {
        atemIPElement.addEventListener('blur', onATEMIPChanged);
    }
    
    // Set up custom change handler for showTally checkbox
    const showTallyElement = document.getElementById('showTally');
    if (showTallyElement) {
        showTallyElement.addEventListener('change', onShowTallyChanged);
    }
    
    // Request ATEM info for current IP if available
    setTimeout(() => {
        const currentIP = atemIPElement?.value;
        if (currentIP && currentIP !== '') {
            requestATEMInfo(currentIP);
        }
    }, 500);
}

// Handle Show Tally checkbox changes
function onShowTallyChanged(event) {
    console.log('Show Tally changed to:', event.target.checked);
    // Force immediate settings save with correct boolean value
    setSettings();
}

// Handle ATEM IP address changes
function onATEMIPChanged(event) {
    const ipAddress = event.target.value;
    if (ipAddress && ipAddress !== '') {
        requestATEMInfo(ipAddress);
    }
}

// Request ATEM capabilities info
function requestATEMInfo(ipAddress) {
    if (websocket && websocket.readyState === 1) {
        sendPayloadToPlugin({
            action: 'getATEMInfo',
            ipAddress: ipAddress
        });
        console.log('ATEM info requested for IP:', ipAddress);
    }
}

// Handle messages from plugin (easypi-v2 will call this automatically)
function websocketOnMessage(evt) {
    var jsonObj = JSON.parse(evt.data);
    
    if (jsonObj.event === 'didReceiveSettings') {
        var payload = jsonObj.payload;
        loadConfiguration(payload.settings);
    }
    else if (jsonObj.event === 'sendToPropertyInspector') {
        handleSendToPropertyInspector(jsonObj.payload);
    }
    else {
        console.log("Ignored websocketOnMessage: " + jsonObj.event);
    }
}

// Handle sendToPropertyInspector events
function handleSendToPropertyInspector(payload) {
    console.log('Received from plugin:', payload);
    
    if (payload.action === 'atemInfoResponse') {
        onATEMInfoReceived(payload);
    }
}

// Handle ATEM info response from plugin
function onATEMInfoReceived(payload) {
    console.log('ATEM info received:', payload);
    
    if (payload && payload.ipAddress) {
        atemInfoCache[payload.ipAddress] = payload;
        
        // Update UI if this is for the current IP
        const currentIP = document.getElementById('atemIPAddress')?.value;
        if (currentIP === payload.ipAddress) {
            updateMixEffectOptions(payload.mixEffectCount || 1);
        }
    }
}

// Update Mix Effect Block options based on ATEM capabilities
function updateMixEffectOptions(meCount) {
    const mixEffectElement = document.getElementById('mixEffectBlock');
    if (!mixEffectElement) return;
    
    const currentValue = mixEffectElement.value;
    
    // Clear existing options
    mixEffectElement.innerHTML = '';
    
    // Add options based on ATEM capabilities
    for (let i = 0; i < meCount; i++) {
        const option = document.createElement('option');
        option.value = i;
        option.textContent = `ME ${i + 1}`;
        mixEffectElement.appendChild(option);
    }
    
    // Restore selection if still valid
    if (currentValue < meCount) {
        mixEffectElement.value = currentValue;
    } else {
        mixEffectElement.value = 0;
        // Trigger settings update
        setSettings();
    }
}

// Load configuration (called by easypi-v2)
function loadConfiguration(settings) {
    console.log('Loading configuration:', settings);
    
    // Load ATEM IP Address
    const atemIPElement = document.getElementById('atemIPAddress');
    if (atemIPElement && settings.atemIPAddress) {
        atemIPElement.value = settings.atemIPAddress;
    }
    
    // Load Mix Effect Block
    const mixEffectElement = document.getElementById('mixEffectBlock');
    if (mixEffectElement && settings.mixEffectBlock !== undefined) {
        mixEffectElement.value = settings.mixEffectBlock;
    }
    
    // Load Show Tally setting with safe boolean conversion
    const showTallyElement = document.getElementById('showTally');
    if (showTallyElement) {
        let showTallyValue = false;
        if (settings.showTally !== undefined) {
            if (typeof settings.showTally === 'boolean') {
                showTallyValue = settings.showTally;
            } else {
                // Handle string values from previous versions
                const tallyStr = settings.showTally.toString().toLowerCase();
                showTallyValue = tallyStr === 'true' || tallyStr === 'on' || tallyStr === '1';
            }
        }
        showTallyElement.checked = showTallyValue;
        console.log('Show Tally loaded as:', showTallyValue);
    }
    
    // Request ATEM info to update UI if IP is available
    if (settings.atemIPAddress) {
        requestATEMInfo(settings.atemIPAddress);
    }
}

// Override setSettings to ensure proper boolean values
function setSettings() {
    const settings = {};
    
    // Get ATEM IP Address
    const atemIPElement = document.getElementById('atemIPAddress');
    if (atemIPElement) {
        settings.atemIPAddress = atemIPElement.value;
    }
    
    // Get Mix Effect Block
    const mixEffectElement = document.getElementById('mixEffectBlock');
    if (mixEffectElement) {
        settings.mixEffectBlock = parseInt(mixEffectElement.value);
    }
    
    // Get Show Tally as proper boolean
    const showTallyElement = document.getElementById('showTally');
    if (showTallyElement) {
        settings.showTally = showTallyElement.checked; // This will be a proper boolean
    }
    
    console.log('Saving settings:', settings);
    
    // Send to plugin
    if (websocket && websocket.readyState === 1) {
        websocket.send(JSON.stringify({
            event: 'setSettings',
            context: uuid,
            payload: settings
        }));
    }
}