// ExtendScript Engine (CEP)

function playSingle(actJsonStr) {
    try {
        var act = eval("(" + actJsonStr + ")");
        if (act.type === "ps_action") {
            if (!act.psSet || !act.psAction) {
                alert("Harap isi Nama Set dan Nama Action.");
                return;
            }
            app.doAction(act.psAction, act.psSet);
        } else if (act.type === "text_replace") {
            // Replace text on currently selected layer if name matches, or just active layer
            var doc = app.activeDocument;
            var layer = doc.activeLayer;
            if (layer.kind == LayerKind.TEXT) {
                layer.textItem.contents = act.newText;
            } else {
                alert("Layer aktif bukan Text Layer.");
            }
        }
    } catch(e) {
        alert("Error: " + e.toString());
    }
}

function playBatch(actJsonStr) {
    try {
        var act = eval("(" + actJsonStr + ")");
        var doc = app.activeDocument;
        var count = 0;
        
        if (act.type === "ps_action") {
            if (!act.psSet || !act.psAction) {
                alert("Harap isi Nama Set dan Nama Action.");
                return;
            }
            // Looping Artboards (Artboards in ExtendScript are just top-level LayerSets usually, or detected via properties)
            // A simple approach: loop all top-level layers
            for (var i = 0; i < doc.layers.length; i++) {
                var artboard = doc.layers[i];
                // Activate the artboard so the action runs inside its context
                doc.activeLayer = artboard;
                try {
                    app.doAction(act.psAction, act.psSet);
                    count++;
                } catch(err) {
                    // action failed on this artboard
                }
            }
            alert("Selesai! Action dijalankan pada " + count + " artboard/group.");
            
        } else if (act.type === "text_replace") {
            if (!act.targetLayer) {
                alert("Harap isi nama Layer Target.");
                return;
            }
            
            // Recursive function to find and replace text
            function replaceTextInLayers(layers) {
                for (var j = 0; j < layers.length; j++) {
                    var layer = layers[j];
                    if (layer.typename === "LayerSet") {
                        replaceTextInLayers(layer.layers);
                    } else if (layer.kind == LayerKind.TEXT && layer.name === act.targetLayer) {
                        layer.textItem.contents = act.newText;
                        count++;
                    }
                }
            }
            
            replaceTextInLayers(doc.layers);
            alert("Selesai! Teks pada " + count + " layer bernama '" + act.targetLayer + "' telah diganti secara masal.");
        }
    } catch(e) {
        alert("Error Batch: " + e.toString());
    }
}
