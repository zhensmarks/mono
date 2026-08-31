const csInterface = new CSInterface();
const fs = require('fs');

let state = {
    selectedActionId: null,
    groups: [
        {
            id: 'g1', name: 'Revisi Artboard',
            actions: [
                { 
                    id: 'a1', name: 'Ubah Text Redaksi', 
                    type: 'text_replace', 
                    psSet: '', psAction: '', 
                    targetLayer: 'Redaksi Sekolah', newText: 'Teks Baru' 
                }
            ]
        }
    ]
};

const els = {
    actionsContainer: document.getElementById('actionsContainer'),
    actionSettings: document.getElementById('actionSettings'),
    settingsTitle: document.getElementById('settingsTitle'),
    selActionType: document.getElementById('selActionType'),
    formPsAction: document.getElementById('formPsAction'),
    formTextReplace: document.getElementById('formTextReplace'),
    inpPsSet: document.getElementById('inpPsSet'),
    inpPsAction: document.getElementById('inpPsAction'),
    inpTargetLayer: document.getElementById('inpTargetLayer'),
    inpNewText: document.getElementById('inpNewText'),
    btnPlaySingle: document.getElementById('btnPlaySingle'),
    btnPlayBatch: document.getElementById('btnPlayBatch')
};

const generateId = () => Math.random().toString(36).substr(2, 9);

function getSelectedAction() {
    for (const g of state.groups) {
        const act = g.actions.find(a => a.id === state.selectedActionId);
        if (act) return act;
    }
    return null;
}

function render() {
    els.actionsContainer.innerHTML = '';
    
    state.groups.forEach(group => {
        const groupEl = document.createElement('div');
        groupEl.className = 'group';
        
        const header = document.createElement('div');
        header.className = 'group-header';
        
        const nameInput = document.createElement('input');
        nameInput.className = 'editable-text';
        nameInput.value = group.name;
        nameInput.onchange = (e) => { group.name = e.target.value; };
        
        const delGroup = document.createElement('button');
        delGroup.className = 'btn-small';
        delGroup.innerText = '🗑️';
        delGroup.onclick = () => { state.groups = state.groups.filter(g => g.id !== group.id); render(); hideSettings(); };
        
        header.appendChild(nameInput);
        header.appendChild(delGroup);
        groupEl.appendChild(header);
        
        const actionsDiv = document.createElement('div');
        actionsDiv.className = 'group-actions';
        
        group.actions.forEach(act => {
            const actEl = document.createElement('div');
            actEl.className = 'action-item';
            if (act.id === state.selectedActionId) actEl.classList.add('selected');
            
            actEl.onclick = (e) => {
                if (e.target.tagName !== 'INPUT' && e.target.tagName !== 'BUTTON') {
                    state.selectedActionId = act.id;
                    render();
                    showSettings(act);
                }
            };
            
            const actNameInput = document.createElement('input');
            actNameInput.className = 'editable-text';
            actNameInput.value = act.name;
            actNameInput.onchange = (e) => { act.name = e.target.value; };
            
            const controls = document.createElement('div');
            controls.className = 'item-controls';
            
            const badge = document.createElement('span');
            badge.innerText = act.type === 'ps_action' ? '🔧 PS Action' : '✏️ Teks';
            badge.style.fontSize = '10px';
            badge.style.color = 'var(--text-muted)';
            
            const delAct = document.createElement('button');
            delAct.className = 'btn-small';
            delAct.innerText = '🗑️';
            delAct.onclick = () => { group.actions = group.actions.filter(a => a.id !== act.id); render(); hideSettings(); };
            
            controls.appendChild(badge);
            controls.appendChild(delAct);
            
            actEl.appendChild(actNameInput);
            actEl.appendChild(controls);
            actionsDiv.appendChild(actEl);
        });
        
        groupEl.appendChild(actionsDiv);
        els.actionsContainer.appendChild(groupEl);
    });
}

function showSettings(act) {
    els.actionSettings.style.display = 'block';
    els.settingsTitle.innerText = `Pengaturan: ${act.name}`;
    
    els.selActionType.value = act.type;
    els.inpPsSet.value = act.psSet || '';
    els.inpPsAction.value = act.psAction || '';
    els.inpTargetLayer.value = act.targetLayer || '';
    els.inpNewText.value = act.newText || '';
    
    updateFormVisibility();
}

function hideSettings() {
    els.actionSettings.style.display = 'none';
}

function updateFormVisibility() {
    if (els.selActionType.value === 'ps_action') {
        els.formPsAction.style.display = 'flex';
        els.formTextReplace.style.display = 'none';
    } else {
        els.formPsAction.style.display = 'none';
        els.formTextReplace.style.display = 'flex';
    }
}

// Listeners for Settings changes
els.selActionType.addEventListener('change', (e) => {
    const act = getSelectedAction();
    if(act) { act.type = e.target.value; updateFormVisibility(); render(); }
});
els.inpPsSet.addEventListener('input', (e) => { const act = getSelectedAction(); if(act) act.psSet = e.target.value; });
els.inpPsAction.addEventListener('input', (e) => { const act = getSelectedAction(); if(act) act.psAction = e.target.value; });
els.inpTargetLayer.addEventListener('input', (e) => { const act = getSelectedAction(); if(act) act.targetLayer = e.target.value; });
els.inpNewText.addEventListener('input', (e) => { const act = getSelectedAction(); if(act) act.newText = e.target.value; });

document.getElementById('btnCloseSettings').addEventListener('click', hideSettings);

// Create New
document.getElementById('btnNewGroup').addEventListener('click', () => {
    state.groups.push({ id: generateId(), name: 'Set Baru', actions: [] });
    render();
});
document.getElementById('btnNewAction').addEventListener('click', () => {
    if (state.groups.length === 0) state.groups.push({ id: generateId(), name: 'Set Default', actions: [] });
    const newAct = { id: generateId(), name: 'Action Baru', type: 'text_replace', psSet: '', psAction: '', targetLayer: '', newText: '' };
    state.groups[state.groups.length - 1].actions.push(newAct);
    state.selectedActionId = newAct.id;
    render();
    showSettings(newAct);
});

// Play execution
els.btnPlaySingle.addEventListener('click', () => {
    const act = getSelectedAction();
    if (!act) return;
    
    const actJson = JSON.stringify(act).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
    csInterface.evalScript('playSingle("' + actJson + '")');
});

els.btnPlayBatch.addEventListener('click', () => {
    const act = getSelectedAction();
    if (!act) return;
    
    const actJson = JSON.stringify(act).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
    csInterface.evalScript('playBatch("' + actJson + '")');
});

// Import Export
document.getElementById('btnExport').addEventListener('click', () => {
    const result = window.cep.fs.showSaveDialogEx('Export Actions', '', ['json'], 'revisi_actions.json', '');
    if (result.err === 0 && result.data) {
        fs.writeFileSync(result.data, JSON.stringify(state.groups, null, 2));
        alert('Export berhasil!');
    }
});
document.getElementById('btnImport').addEventListener('click', () => {
    const result = window.cep.fs.showOpenDialogEx(false, false, 'Import Actions', '', ['json']);
    if (result.err === 0 && result.data && result.data.length > 0) {
        try {
            const content = fs.readFileSync(result.data[0], 'utf-8');
            state.groups = JSON.parse(content);
            render(); alert('Import berhasil!');
        } catch (e) { alert('Gagal membaca JSON.'); }
    }
});

render();
