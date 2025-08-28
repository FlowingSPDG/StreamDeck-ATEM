let websocket = null;
let uuid = null;

function connectElgatoStreamDeckSocket(inPort, inPropertyInspectorUUID, inRegisterEvent, inInfo, inActionInfo) {
    uuid = inPropertyInspectorUUID;
    websocket = new WebSocket('ws://localhost:' + inPort);

    websocket.onopen = function () {
        var json = {
            event: inRegisterEvent,
            uuid: inPropertyInspectorUUID
        };
        websocket.send(JSON.stringify(json));
        requestSettings();
    };

    websocket.onmessage = function (evt) {
        var jsonObj = JSON.parse(evt.data);
        if (jsonObj.event === 'didReceiveSettings') {
            var payload = jsonObj.payload;
            loadSettings(payload.settings);
        }
    };
}

function requestSettings() {
    if (websocket && websocket.readyState === 1) {
        const json = {
            event: 'getSettings',
            context: uuid
        };
        websocket.send(JSON.stringify(json));
    }
}

function loadSettings(settings) {
    console.log('Loading settings:', settings);

    document.getElementById('atemIPAddress').value = settings.atemIPAddress || '192.168.1.101';
    document.getElementById('mixEffectBlock').value = settings.mixEffectBlock || 0;
    document.getElementById('inputId').value = settings.inputId || 1;
    document.getElementById('showTally').checked = settings.showTally !== undefined ? settings.showTally : true;
}

function setSettings() {
    var payload = {};
    payload.atemIPAddress = document.getElementById('atemIPAddress').value;
    payload.mixEffectBlock = parseInt(document.getElementById('mixEffectBlock').value);
    payload.inputId = parseInt(document.getElementById('inputId').value);
    payload.showTally = document.getElementById('showTally').checked;

    if (websocket && websocket.readyState === 1) {
        const json = {
            event: 'setSettings',
            context: uuid,
            payload: payload
        };
        websocket.send(JSON.stringify(json));
    }
}