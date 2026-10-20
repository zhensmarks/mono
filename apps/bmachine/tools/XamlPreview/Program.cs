using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml; // AvaloniaRuntimeXamlLoader (assembly dari paket Avalonia.Markup.Xaml.Loader)
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace XamlPreview;

/// <summary>
/// Previewer XAML ala VS Code Avalonia extension: render view/snipet XAML BMachine ke PNG
/// tanpa menjalankan app penuh (Avalonia headless + resource styles App.axaml asli).
///
/// Pakai:
///   XamlPreview --type RadialMenuWindow --out preview.png [--w 1000] [--h 700] [--theme dark|light]
///   XamlPreview --file path/view.axaml --out preview.png
///   XamlPreview --type BMachine.UI.Views.CommentListView --out preview.png --theme light
/// </summary>
internal static class Program
{
    private static string? Next(string[] args, ref int i)
        => i < args.Length ? args[i++] : null;

    private static int Main(string[] args)
    {
        string? typeName = null, filePath = null, outPath = "preview.png", appAxamlPath = null;
        int width = 1000, height = 700;
        string theme = "dark";

        int i = 0;
        while (i < args.Length)
        {
            var a = args[i++];
            switch (a)
            {
                case "--type": typeName = Next(args, ref i); break;
                case "--file": filePath = Next(args, ref i); break;
                case "--out": outPath = Next(args, ref i) ?? outPath; break;
                case "--w": int.TryParse(Next(args, ref i), out width); break;
                case "--h": int.TryParse(Next(args, ref i), out height); break;
                case "--theme": theme = (Next(args, ref i) ?? "dark").ToLowerInvariant(); break;
                case "--app-axaml": appAxamlPath = Next(args, ref i); break;
                case "--help" or "-h":
                    Console.WriteLine("pakai: XamlPreview --type <NamaView>|--file <path.axaml> [--out p.png] [--w N] [--h N] [--theme dark|light]");
                    return 0;
                default:
                    Console.Error.WriteLine($"arg tak dikenal: {a}");
                    return 2;
            }
        }

        if (typeName is null && filePath is null)
        {
            Console.Error.WriteLine("butuh --type atau --file (lihat --help)");
            return 2;
        }

        try
        {
            // Paksa assembly ke-load dulu: resolver XAML runtime butuh assembly sudah berada
            // di ALC (BMachine.UI, Semi, ColorPicker, DataGrid) untuk memuaskan avares:// + using:
            foreach (var asmName in new[]
                     {
                         "BMachine.UI", "Semi.Avalonia", "Avalonia.Controls.ColorPicker",
                         "Avalonia.Controls.DataGrid", "Avalonia.Fonts.Inter",
                     })
            {
                try { Assembly.Load(new AssemblyName(asmName)); }
                catch { /* tidak fatal; error parse akan yang mengadu */ }
            }

            // ---------------------------------------------------------------- App.axaml asli
            var appPath = appAxamlPath ?? FindAppAxaml() ?? throw new InvalidOperationException(
                "App.axaml tidak ditemukan; jalankan dari dalam repo mono atau pakai --app-axaml <path>");
            var appText = File.ReadAllText(appPath);
            // x:Class dibuang supaya bisa diparse jadi Application lepas (tanpa class BMachine.App.App)
            appText = Regex.Replace(appText, @"\s+x:Class=""[^""]+""", "");

            var builder = AppBuilder.Configure(() =>
                {
                    var app = AvaloniaRuntimeXamlLoader.Parse<Application>(appText);
                    app.RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
                    return app;
                })
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()          // UseHeadlessDrawing=false butuh backend render nyata (Skia)
                .WithInterFont();
            builder.SetupWithoutStarting();

            // ---------------------------------------------------------------- konten
            var content = CreateContent(typeName, filePath, width, height);

            var win = content as Window ?? new Window
            {
                Width = width,
                Height = height,
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = content,
            };

            win.Show();
            win.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            win.UpdateLayout();

            var pw = Math.Max(1, (int)win.Bounds.Width);
            var ph = Math.Max(1, (int)win.Bounds.Height);
            using var bmp = new RenderTargetBitmap(new PixelSize(pw, ph));
            bmp.Render(win);

            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            bmp.Save(outPath);

            Console.WriteLine($"OK {pw}x{ph} -> {Path.GetFullPath(outPath)}");
            Environment.Exit(0);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PREVIEW GAGAL: " + ex.GetType().Name + ": " + ex.Message);
            if (ex.InnerException != null) Console.Error.WriteLine("  inner: " + ex.InnerException.Message);
            return 1;
        }
    }

    private static Control CreateContent(string? typeName, string? filePath, int width, int height)
    {
        // kumpulkan kandidat type dari assembly BMachine.UI (referensi keras)
        Assembly ui = typeof(BMachine.UI.Views.RadialMenuWindow).Assembly;

        if (typeName != null)
        {
            var t = Resolve(ui, typeName) ?? throw new InvalidOperationException(
                $"type '{typeName}' tidak ditemukan di BMachine.UI");
            return Materialize(t, width, height);
        }

        // --file: x:Class -> instantiate view; tanpa x:Class -> parse jadi snipet
        var text = File.ReadAllText(filePath!);
        var m = Regex.Match(text, @"x:Class=""([^""]+)""");
        if (m.Success)
        {
            var t = Resolve(ui, m.Groups[1].Value) ?? throw new InvalidOperationException(
                $"x:Class '{m.Groups[1].Value}' tidak ditemukan di BMachine.UI");
            return Materialize(t, width, height);
        }

        var parsed = AvaloniaRuntimeXamlLoader.Parse<object>(Regex.Replace(text, @"\s+x:Class=""[^""]+""", ""));
        return parsed switch
        {
            Window w => w,        // Window juga Control — cek dulu
            Control c => c,
            _ => throw new InvalidOperationException("akar snipet bukan Control: " + parsed?.GetType().FullName),
        };
    }

    private static Control Materialize(Type t, int width, int height)
    {
        if (typeof(Window).IsAssignableFrom(t))
            return (Window)Activator.CreateInstance(t)!;   // Window sudah punya size sendiri

        if (!typeof(Control).IsAssignableFrom(t))
            throw new InvalidOperationException($"{t.FullName} bukan Control/Window");

        var obj = Activator.CreateInstance(t);
        return obj as Control ?? throw new InvalidOperationException("gagal membuat instance " + t.FullName);
    }

    private static Type? Resolve(Assembly asm, string name)
    {
        var direct = asm.GetType(name, throwOnError: false);
        if (direct != null) return direct;

        Type?[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }

        return types.FirstOrDefault(t =>
            t != null && (t.FullName == name || t.Name == name));
    }

    private static string? FindAppAxaml()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                foreach (var rel in new[]
                         {
                             Path.Combine("apps", "bmachine", "src", "BMachine.App", "App.axaml"),
                             Path.Combine("src", "BMachine.App", "App.axaml"),
                             Path.Combine("BMachine.App", "App.axaml"),
                         })
                {
                    var p = Path.Combine(dir.FullName, rel);
                    if (File.Exists(p)) return p;
                }
                dir = dir.Parent;
            }
        }
        return null;
    }
}
