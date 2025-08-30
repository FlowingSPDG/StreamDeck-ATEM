// Auto Transition Action Property Inspector using easypi-v2

var settingsCache = {};

// Initialize PropertyInspector after WebSocket connection
function initPropertyInspector() {
    console.log('Auto Transition PI initialized');
    
    // Set up event listeners for form elements
    const elements = document.getElementsByClassName('sdProperty');
    Array.prototype.forEach.call(elements, function(elem) {
        if (elem.type === 'checkbox') {
            elem.addEventListener('change', setSettings);
        } else {
            elem.addEventListener('input', setSettings);
            elem.addEventListener('change', setSettings);
        }
    });
    
    // Request current settings from plugin after a short delay to ensure websocket is ready
    setTimeout(requestSettings, 100);
}

// Override the loadConfiguration function to handle our specific settings
function loadConfiguration(payload) {
    console.log('Loading Auto Transition configuration:', payload);
    
    // Cache the settings
    settingsCache = payload || {};
    
    // Set default values if not present
    if (settingsCache.atemIPAddress === undefined) settingsCache.atemIPAddress = '192.168.1.101';
    if (settingsCache.mixEffectBlock === undefined) settingsCache.mixEffectBlock = 0;
    
    // Load values into form elements
    updateFormElements(settingsCache);
}

function updateFormElements(settings) {
    // Update ATEM IP Address
    const atemIPElement = document.getElementById('atemIPAddress');
    if (atemIPElement) {
        atemIPElement.value = settings.atemIPAddress || '192.168.1.101';
    }
    
    // Update Mix Effect Block
    const mixEffectElement = document.getElementById('mixEffectBlock');
    if (mixEffectElement) {
        mixEffectElement.value = settings.mixEffectBlock !== undefined ? settings.mixEffectBlock : 0;
    }
    
    console.log('Form elements updated with settings:', settings);
}

// Request current settings from the plugin
function requestSettings() {
    if (websocket && websocket.readyState === 1) {
        const json = {
            event: 'getSettings',
            context: uuid
        };
        websocket.send(JSON.stringify(json));
        console.log('Settings requested from plugin');
    }
}

// Override setSettings to ensure proper data types
function setSettings() {
    var payload = {};
    var elements = document.getElementsByClassName("sdProperty");

    Array.prototype.forEach.call(elements, function (elem) {
        var key = elem.id;
        var value = elem.value;
        
        if (elem.type === 'checkbox') {
            payload[key] = elem.checked;
        } else if (elem.type === 'number') {
            // Convert numeric inputs to proper types
            if (key === 'mixEffectBlock') {
                payload[key] = parseInt(value) || 0;
            } else {
                payload[key] = parseFloat(value) || 0;
            }
        } else {
            payload[key] = value;
        }
        
        console.log("Setting: " + key + " = " + payload[key] + " (type: " + typeof payload[key] + ")");
    });
    
    // Update cache
    settingsCache = Object.assign(settingsCache, payload);
    
    // Send to plugin
    setSettingsToPlugin(payload);
}

// Note: Do NOT override connectElgatoStreamDeckSocket - it's already implemented in sdtools.common.js
// The function is automatically called by Stream Deck when the Property Inspector loads