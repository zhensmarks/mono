document.addEventListener("DOMContentLoaded", function() {
    var inputField = document.getElementById("calc-input");
    var outputDisplay = document.getElementById("output");

    // Enter opsional hanya untuk memblok/highlight teks (biar mudah ketik angka baru)
    inputField.addEventListener("keydown", function(e) {
        if (e.key === "Enter") {
            inputField.select();
        }
    });

    // Fitur PINTAR: Hitung otomatis secara langsung setiap kali pengguna mengetik (Live Calculation)
    inputField.addEventListener("input", function() {
        var val = inputField.value.trim();
        
        if (val === "") {
            outputDisplay.textContent = "-";
        } else {
            var result = processCalculation(val);
            if (result !== null) {
                outputDisplay.textContent = result;
            } else {
                // Jika masih dalam proses mengetik (misal baru "17/"), biarkan kosong atau tampilkan strip
                outputDisplay.textContent = "-";
            }
        }
    });

    function processCalculation(expr) {
        // Cek apakah formatnya pembagian, contoh: 17/2 atau 17 / 2
        var divMatch = expr.match(/^(\d+)\s*\/\s*(\d+)$/);
        
        if (divMatch) {
            var num = parseInt(divMatch[1], 10);
            var denom = parseInt(divMatch[2], 10);
            
            if (denom === 0) return null; // cegah divide by zero error saat mengetik 0
            if (denom > num) return "0"; 
            
            var quotient = Math.floor(num / denom);
            var remainder = num % denom;
            
            var parts = [];
            
            for(var i = 0; i < remainder; i++) {
                parts.push(quotient + 1);
            }
            
            for(var j = 0; j < denom - remainder; j++) {
                parts.push(quotient);
            }
            
            return parts.join("+");
        }
        
        // Jangan dievaluasi jika teks diakhiri dengan simbol operator (menandakan masih proses ngetik)
        if (/[+\-*/.]$/.test(expr.trim())) {
            return null;
        }
        
        // Jika format lain (seperti perkalian biasa, tambah, kurang)
        try {
            // Evaluasi sederhana jika mengandung angka dan operator standar
            if(/^[0-9+\-*/().\s]+$/.test(expr)) {
                var result = eval(expr);
                
                // Jangan tampilkan jika hasilnya undefined
                if (result === undefined) return null;
                
                // Jika hasilnya desimal, limit maksimal 2 angka di belakang koma
                if (result % 1 !== 0) {
                    result = parseFloat(result.toFixed(2)); 
                }
                return result;
            }
        } catch(e) {
            return null; // Abaikan error ketikan belum lengkap
        }
        
        return null;
    }
});
