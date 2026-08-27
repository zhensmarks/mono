#target photoshop

function getSelectedItemInfo() {
    if (app.documents.length === 0) {
        return "0|LAYER";
    }

    try {
        var selectedIDs = getSelectedLayerIDs();
        var layerCount = 0;
        var groupCount = 0;

        for (var i = 0; i < selectedIDs.length; i++) {
            if (isGroupLayer(selectedIDs[i])) {
                groupCount++;
            } else {
                layerCount++;
            }
        }

        if (groupCount > 0 && layerCount === 0) {
            return String(groupCount) + "|GROUP";
        }

        if (layerCount > 0 && groupCount === 0) {
            return String(layerCount) + "|LAYER";
        }

        return String(layerCount + groupCount) + "|ITEM";
    } catch (e) {
        return "0|LAYER";
    }
}

function getSelectedLayerIDs() {
    var ref = new ActionReference();
    ref.putProperty(stringIDToTypeID("property"), stringIDToTypeID("targetLayersIDs"));
    ref.putEnumerated(
        stringIDToTypeID("document"),
        stringIDToTypeID("ordinal"),
        stringIDToTypeID("targetEnum")
    );

    var desc = executeActionGet(ref);
    var ids = [];

    if (desc.hasKey(stringIDToTypeID("targetLayersIDs"))) {
        var list = desc.getList(stringIDToTypeID("targetLayersIDs"));
        for (var i = 0; i < list.count; i++) {
            ids.push(list.getReference(i).getIdentifier());
        }
    } else {
        var activeRef = new ActionReference();
        activeRef.putProperty(stringIDToTypeID("property"), stringIDToTypeID("layerID"));
        activeRef.putEnumerated(
            stringIDToTypeID("layer"),
            stringIDToTypeID("ordinal"),
            stringIDToTypeID("targetEnum")
        );
        ids.push(executeActionGet(activeRef).getInteger(stringIDToTypeID("layerID")));
    }

    return ids;
}

function isGroupLayer(layerID) {
    var ref = new ActionReference();
    ref.putProperty(stringIDToTypeID("property"), stringIDToTypeID("layerSection"));
    ref.putIdentifier(stringIDToTypeID("layer"), layerID);

    var desc = executeActionGet(ref);
    var section = desc.getEnumerationValue(stringIDToTypeID("layerSection"));
    return section === stringIDToTypeID("layerSectionStart");
}
