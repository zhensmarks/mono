// Scripts/Action/place_on_layer.jsx
/*
  BMachine Place on Layer Script
  Reads target file path from temp file 'bmachine_place_target.txt' and places it into the current document.
  If no document is open, it opens the file.
*/
#target photoshop

function main() {
    var tempPath = Folder.temp + "/bmachine_place_target.txt";
    var file = new File(tempPath);
    if (!file.exists) {
        // silently fail or alert if debugging
        return;
    }

    file.open("r");
    var targetPath = file.read();
    file.close();

    // Clean up temp file? Optional.
    // file.remove();

    if (!targetPath) return;

    var targetFile = new File(targetPath);
    if (targetFile.exists) {
        placeFile(targetFile);
    }
}

function placeFile(file) {
    var hasActiveDoc = false;
    try {
        var doc = app.activeDocument;
        hasActiveDoc = true;
    } catch (e) {
        hasActiveDoc = false;
    }

    if (!hasActiveDoc) {
        open(file);
        return;
    }

    try {
        var idOlac = charIDToTypeID("Plc ");
        var desc2 = new ActionDescriptor();
        var idnull = charIDToTypeID("null");
        desc2.putPath(idnull, file);
        var idFTcs = charIDToTypeID("FTcs");
        var idQCSt = charIDToTypeID("QCSt");
        var idQcsa = charIDToTypeID("Qcsa");
        desc2.putEnumerated(idFTcs, idQCSt, idQcsa);
        // Offset logic omitted to place at center default
        executeAction(idOlac, desc2, DialogModes.NO);
    } catch (e) {
        alert("Error placing file: " + e);
    }
}

main();
