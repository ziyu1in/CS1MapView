using CS2MapView.Beta;
using CS2MapView.Data;
using CS2MapView.Form;
using SkiaSharp;
using System.Reflection;
using System.Text.Json;

/// <summary>Opt-in hardware check using the real MainForm and its message loop.</summary>
internal static class RoadOpenGLChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run(string map, string reports)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        BetaSettings.Load();
        var config = BetaSettings.Config;
        config.SelectedStyle = "carto-voyager";
        config.CanvasSize = 16384;
        config.RenderTransport = config.RenderTransportRoute = false;
        config.RenderHillshading = config.DisplayContourLines = false;
        var output = Path.Combine(reports, "road-opengl"); Directory.CreateDirectory(output);
        var results = new List<object>();
        int exit = 1;
        using var main = new MainForm { Size = new(1400, 950), StartPosition = FormStartPosition.Manual, Location = new(70, 70) };
        main.Shown += async (_, _) =>
        {
            try
            {
                await main.OpenMapAsync(map);
                if (main.MapData is null) throw new Exception("MainForm could not load the road check export.");
                var control = (MapGLControl)typeof(MainForm).GetField("SkiaControl", Private)!.GetValue(main)!;
                TaskCompletionSource<SKBitmap>? pending = null;
                control.PaintSurface += (_, e) =>
                {
                    if (pending is null) return;
                    var request = pending; pending = null;
                    var bitmap = new SKBitmap(e.Info.Width, e.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    if (e.Surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) request.SetResult(bitmap);
                    else { bitmap.Dispose(); request.SetException(new Exception("Live GPU readback failed.")); }
                };
                async Task Capture(string name)
                {
                    pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var next = pending.Task; control.Invalidate();
                    using var gpu = await next.WaitAsync(TimeSpan.FromSeconds(20));
                    using var cpu = new SKBitmap(gpu.Info);
                    using (var canvas = new SKCanvas(cpu))
                        main.Context.DrawLayers(main.MapData!, canvas, new SKRect(0, 0, cpu.Width, cpu.Height));
                    var a = cpu.Pixels; var b = gpu.Pixels;
                    int missing = 0;
                    // Ignore stroke/text antialiasing: only count flat, opaque interiors.
                    for (int y = 1; y < cpu.Height - 1; y++) for (int x = 1; x < cpu.Width - 1; x++)
                    {
                        int i = y * cpu.Width + x; var c = a[i]; var d = b[i];
                        if (c.Alpha != 255 || a[i-1] != c || a[i+1] != c || a[i-cpu.Width] != c || a[i+cpu.Width] != c) continue;
                        if (Math.Max(Math.Abs(c.Red-d.Red), Math.Max(Math.Abs(c.Green-d.Green), Math.Abs(c.Blue-d.Blue))) >= 24) missing++;
                    }
                    results.Add(new { name, gpu.Width, gpu.Height, missing });
                    using var image = SKImage.FromBitmap(gpu); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using (var file = File.Create(Path.Combine(output, name + ".png"))) data.SaveTo(file);
                    Console.WriteLine($"OpenGL {name}: {missing} mismatched interior pixels");
                    if (missing > 16) throw new Exception($"Live OpenGL road gaps in {name}: {missing} interior pixels.");
                }
                var raw = ((CS1MapType)main.Context.SourceMapType!).GetImportData();
                var port = raw.BuildingList.FirstOrDefault(b => b.Name == "Harbor02-Monorail Hub");
                if (port is null) throw new Exception("This regression check requires an export containing the passenger-harbor hub asset.");
                float px = port.Points.Average(p => p.X), py = -port.Points.Average(p => p.Z);
                var view = main.Context.ViewContext;
                foreach (var style in new[] { "carto-voyager", "carto-positron" })
                {
                    await main.ApplyMenuFeatureAsync(() => main.Session!.Config.SelectedStyle = style);
                    foreach (var zoom in new[] { 1.5f, 3f })
                    {
                        view.ScaleFactor = zoom; view.Angle = 0;
                        view.ViewLeftTop = (px - control.Width / view.ViewScaleFromWorld * .55f, py - control.Height / view.ViewScaleFromWorld * .68f);
                        main.OnZoomOrViewPositionChanged();
                        await (Task)typeof(MainForm).GetMethod("RebuildViewAsync", Private)!.Invoke(main, [false])!;
                        await Capture($"{style}-zoom-{zoom}");
                        typeof(MainForm).GetMethod("WheelZoom", Private)!.Invoke(main, [new SKPoint(control.Width * .55f, control.Height * .68f), 120]);
                        await Capture($"{style}-wheel-{zoom}-immediate");
                        await Task.Delay(750);
                        await Capture($"{style}-wheel-{zoom}-settled");
                        foreach (var (method, buttons, clicks, x, y) in new[] {
                            ("SkiaControl_MouseDown", MouseButtons.Left, 1, 600, 450),
                            ("SkiaControl_MouseMove", MouseButtons.Left, 0, 655, 480),
                            ("SkiaControl_MouseUp", MouseButtons.Left, 1, 655, 480) })
                            typeof(MainForm).GetMethod(method, Private)!.Invoke(main, [control, new MouseEventArgs(buttons, clicks, x, y, 0)]);
                        await Capture($"{style}-pan-{zoom}");
                    }
                    main.Size = new(1200, 800); await Task.Delay(750);
                    await Capture(style + "-resize"); await Capture(style + "-resize-repeat");
                    main.Size = new(1400, 950); await Task.Delay(750);
                    var previous = control.MapContext;
                    typeof(Control).GetMethod("RecreateHandle", Private)!.Invoke(control, null);
                    await Task.Delay(750);
                    await Capture(style + "-recreated-handle"); await Capture(style + "-recreated-handle-repeat");
                    if (previous is null || control.MapContext is null || ReferenceEquals(previous, control.MapContext))
                        throw new Exception("Map OpenGL context did not recreate with its window handle.");
                }
                exit = 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); }
            finally
            {
                File.WriteAllText(Path.Combine(output, "frames.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                main.Close();
            }
        };
        Application.Run(main);
        return exit;
    }
}
