var csInterface = new CSInterface();
var requestInProgress = false;

function updateSelectionCount() {
    if (requestInProgress) {
        return;
    }

    requestInProgress = true;
    csInterface.evalScript("getSelectedItemInfo()", function(result) {
        requestInProgress = false;

        var parts = String(result || "0|LAYER").split("|");
        document.getElementById("count").textContent = parts[0] || "0";
        document.getElementById("item-type").textContent = parts[1] || "LAYER";
    });
}

updateSelectionCount();
setInterval(updateSelectionCount, 500);
