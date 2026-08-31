const { app, core, action } = require("photoshop");
const fs = require("uxp").storage.localFileSystem;

// State management
let state = {
    isRecording: false,
    selectedActionId: 'a1',
    groups: [
        {
            id: 'g1',
            name: 'Default Set',
            actions: [
                { id: 'a1', name: 'Ganti Redaksi', steps: [] }
            ]
        }
    ]
};

// UI Elements
const els = {
    btnRecord: document.getElementById('btnRecord'),
    btnStop: document.getElementById('btnStop'),
    btnPlaySingle: document.getElementById('btnPlaySingle'),
    btnPlayBatch: document.getElementById('btnPlayBatch'),
    btnNewGroup: document.getElementById('btnNewGroup'),
    btnNewAction: document.getElementById('btnNewAction'),
    btnImport: document.getElementById('btnImport'),
    btnExport: document.getElementById('btnExport'),
    actionsContainer: document.getElementById('actionsContainer')
};

// Helper to generate IDs
const generateId = () => Math.random().toString(36).substr(2, 9);

// Render the UI based on state
function render() {
    els.actionsContainer.innerHTML = '';
    
    // Update button states
    els.btnRecord.style.color = state.isRecording ? 'var(--danger)' : 'var(--text)';
    
    state.groups.forEach(group => {
        const groupEl = document.createElement('div');
        groupEl.className = 'group';
        
        // Group Header
        const header = document.createElement('div');
        header.className = 'group-header';
        
        const nameInput = document.createElement('input');
        nameInput.className = 'editable-text';
        nameInput.value = group.name;
        nameInput.onchange = (e) => { group.name = e.target.value; };
        
        const deleteGroupBtn = document.createElement('button');
        deleteGroupBtn.className = 'btn-small';
        deleteGroupBtn.innerText = '🗑️';
        deleteGroupBtn.onclick = () => {
            state.groups = state.groups.filter(g => g.id !== group.id);
            render();
        };
        
        header.appendChild(nameInput);
        header.appendChild(deleteGroupBtn);
        groupEl.appendChild(header);
        
        // Actions container
        const actionsDiv = document.createElement('div');
        actionsDiv.className = 'group-actions';
        
        group.actions.forEach(act => {
            const actEl = document.createElement('div');
            actEl.className = 'action-item';
            if (act.id === state.selectedActionId) actEl.classList.add('selected');
            if (state.isRecording && act.id === state.selectedActionId) actEl.classList.add('recording');
            
            actEl.onclick = (e) => {
                if (e.target.tagName !== 'INPUT' && e.target.tagName !== 'BUTTON') {
                    state.selectedActionId = act.id;
                    render();
                }
            };
            
            const actNameInput = document.createElement('input');
            actNameInput.className = 'editable-text';
            actNameInput.value = act.name;
            actNameInput.onchange = (e) => { act.name = e.target.value; };
            
            const controls = document.createElement('div');
            controls.className = 'item-controls';
            
            const stepCountSpan = document.createElement('span');
            stepCountSpan.innerText = `(${act.steps.length})`;
            stepCountSpan.style.marginRight = '8px';
            stepCountSpan.style.opacity = '0.5';
            
            const deleteActBtn = document.createElement('button');
            deleteActBtn.className = 'btn-small';
            deleteActBtn.innerText = '🗑️';
            deleteActBtn.onclick = () => {
                group.actions = group.actions.filter(a => a.id !== act.id);
                render();
            };
            
            controls.appendChild(stepCountSpan);
            controls.appendChild(deleteActBtn);
            
            actEl.appendChild(actNameInput);
            actEl.appendChild(controls);
            actionsDiv.appendChild(actEl);
        });
        
        groupEl.appendChild(actionsDiv);
        els.actionsContainer.appendChild(groupEl);
    });
}

// Get currently selected action object
function getSelectedAction() {
    for (const g of state.groups) {
        const act = g.actions.find(a => a.id === state.selectedActionId);
        if (act) return act;
    }
    return null;
}

// Photoshop Notification Listener for Recording
const notificationListener = (event, descriptor) => {
    if (!state.isRecording) return;
    // Prevent self-trigger loops
    if (descriptor && descriptor._isRevisi) return;
    
    // Ignore select events if not useful, or keep them to replicate exact selections
    const ignoredEvents = ['select', 'invokeCommand', 'modalStateChanged']; 
    if (ignoredEvents.includes(event)) return;
    
    const activeAction = getSelectedAction();
    if (activeAction) {
        activeAction.steps.push({ event, descriptor });
        render(); // update step count
    }
};

// Add listener
action.addNotificationListener(['all'], notificationListener);

// Button Handlers
els.btnRecord.addEventListener('click', () => {
    if (!state.selectedActionId) {
        app.showAlert("Please select an action to record into.");
        return;
    }
    state.isRecording = true;
    render();
});

els.btnStop.addEventListener('click', () => {
    state.isRecording = false;
    render();
});

els.btnNewGroup.addEventListener('click', () => {
    state.groups.push({ id: generateId(), name: 'New Set', actions: [] });
    render();
});

els.btnNewAction.addEventListener('click', () => {
    if (state.groups.length === 0) {
        state.groups.push({ id: generateId(), name: 'Default Set', actions: [] });
    }
    const newAct = { id: generateId(), name: 'New Action', steps: [] };
    state.groups[state.groups.length - 1].actions.push(newAct);
    state.selectedActionId = newAct.id;
    render();
});

// Play logic
async function playAction(act, doc) {
    if (!act || act.steps.length === 0) return;
    
    for (const step of act.steps) {
        try {
            // Re-run the recorded descriptor
            // We append a flag to prevent our listener from re-recording this
            const desc = Object.assign({}, step.descriptor, { _isRevisi: true });
            await core.executeAsModal(async () => {
                await action.batchPlay([{ _obj: step.event, ...desc }], {});
            }, { "commandName": "Revisi Play: " + act.name });
        } catch (e) {
            console.error("Error playing step:", e);
        }
    }
}

els.btnPlaySingle.addEventListener('click', async () => {
    const act = getSelectedAction();
    if (!act) return;
    await playAction(act, app.activeDocument);
});

els.btnPlayBatch.addEventListener('click', async () => {
    const act = getSelectedAction();
    if (!act) return;
    
    const doc = app.activeDocument;
    if (!doc) {
        app.showAlert("No active document!");
        return;
    }
    
    // In a PSD with artboards, we can look for all artboards
    // A simplified batch execution:
    // Finding all layer groups that are artboards, then selecting them one by one, 
    // and running the action steps. 
    // For now, this is a placeholder that simply loops layers.
    app.showAlert("Batch Play: Target detected. Logic to loop artboards is ready to be expanded here.");
    
    // Pseudo-code for actual batch:
    /*
    await core.executeAsModal(async () => {
        doc.layers.forEach(layer => {
            if (layer.isArtboard) {
                // Select layer/artboard
                // Play action
            }
        })
    });
    */
});

// Export/Import JSON state
els.btnExport.addEventListener('click', async () => {
    const folder = await fs.getFolder();
    if (!folder) return;
    const file = await folder.createFile("revisi_actions.json", { overwrite: true });
    await file.write(JSON.stringify(state.groups, null, 2));
    app.showAlert("Exported successfully!");
});

els.btnImport.addEventListener('click', async () => {
    const files = await fs.getFileForOpening({ types: ["json"] });
    if (!files || files.length === 0) return;
    const content = await files[0].read();
    try {
        state.groups = JSON.parse(content);
        render();
        app.showAlert("Imported successfully!");
    } catch (e) {
        app.showAlert("Failed to parse JSON.");
    }
});

// Initial render
render();
