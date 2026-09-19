using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace PixelcutCompact.Helpers;

public static class PlaywrightStealthHelper
{
    public static async Task ApplyStealthSettingsAsync(IBrowserContext context)
    {
        await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            { "Accept-Language", "en-US,en;q=0.9,id;q=0.8" }
        });

        await context.AddInitScriptAsync(@"
            // 1. Overwrite webdriver
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined
            });

            // 2. Bypass navigator.languages
            Object.defineProperty(navigator, 'languages', {
                get: () => ['en-US', 'en', 'id']
            });

            // 3. Mock Chrome runtime properties
            window.chrome = {
                runtime: {},
                loadTimes: function() {},
                csi: function() {},
                app: {}
            };

            // 4. Mock navigator.plugins
            Object.defineProperty(navigator, 'plugins', {
                get: () => [
                    { name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' },
                    { name: 'Chromium PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' }
                ]
            });

            // 5. Samarkan WebGL Renderer (Menghindari deteksi GPU virtual)
            const getParameter = WebGLRenderingContext.prototype.getParameter;
            WebGLRenderingContext.prototype.getParameter = function(parameter) {
                // UNMASKED_VENDOR_WEBGL
                if (parameter === 37445) return 'Google Inc. (NVIDIA)';
                // UNMASKED_RENDERER_WEBGL
                if (parameter === 37446) return 'ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)';
                return getParameter.call(this, parameter);
            };

            // 6. Sinkronisasi status Permission Notifications
            const originalQuery = navigator.permissions.query;
            navigator.permissions.query = (parameters) =>
                parameters.name === 'notifications'
                    ? Promise.resolve({ state: Notification.permission })
                    : originalQuery(parameters);
        ");
    }
}
