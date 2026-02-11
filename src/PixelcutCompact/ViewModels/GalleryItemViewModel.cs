using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Media;
using PixelcutCompact.Models;

namespace PixelcutCompact.ViewModels;

public partial class GalleryItemViewModel : ObservableObject, System.IDisposable
{
    private static readonly System.Threading.SemaphoreSlim _thumbnailSemaphore = new(3); // Limit to 3 concurrent loads

    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _fileName;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private PixelcutFileItem _parentItem;
    [ObservableProperty] private bool _isSource; // True = JPG/Input, False = PNG/Output

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnail == null) LoadThumbnailAsync();
            return _thumbnail;
        }
        private set => SetProperty(ref _thumbnail, value);
    }

    private bool _isLoadingThumbnail;
    private bool _isDisposed;

    public GalleryItemViewModel(PixelcutFileItem parent, string path, bool isSource)
    {
        ParentItem = parent;
        FilePath = path;
        IsSource = isSource;
        FileName = Path.GetFileName(path);
        
        // Initial Sync
        _isSelected = parent.IsSelected;
        
        // Listen to Parent
        ParentItem.PropertyChanged += OnParentPropertyChanged;
    }

    private void OnParentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isDisposed) return;
        if (e.PropertyName == nameof(PixelcutFileItem.IsSelected))
        {
            if (IsSelected != ParentItem.IsSelected)
            {
                IsSelected = ParentItem.IsSelected;
            }
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (_isDisposed) return;
        // Update Parent
        if (ParentItem.IsSelected != value)
        {
            ParentItem.IsSelected = value;
        }
    }

    [ObservableProperty] private double _rotationAngle;

    private async void LoadThumbnailAsync()
    {
        if (_isLoadingThumbnail || _thumbnail != null || _isDisposed) return;
        _isLoadingThumbnail = true;

        if (!File.Exists(FilePath)) return;

        try
        {
            await _thumbnailSemaphore.WaitAsync();
            if (_isDisposed) return;
            
            await Task.Run(async () =>
            {
                try 
                {
                    // Read Orientation efficiently
                    int orientation = 1;
                    if (IsSource)
                    {
                         try { orientation = Helpers.ExifHelper.GetOrientation(FilePath); } catch { }
                    }
                    
                    // Decode properly scaled image (Target width 200px)
                    Bitmap? finalBitmap = null;
                    
                    using (var fs = File.OpenRead(FilePath))
                    {
                        // Avalonia DecodeToWidth is efficient
                        finalBitmap = Bitmap.DecodeToWidth(fs, 200, BitmapInterpolationMode.MediumQuality);
                    }

                    if (finalBitmap == null) return;
                    
                    // Apply Rotation ONLY if needed (Expensive operation)
                    if (orientation == 6 || orientation == 8 || orientation == 3) // 90, 270, 180
                    {
                         // ... (Dimensions calc) ...
                         var w = finalBitmap.PixelSize.Width;
                         var h = finalBitmap.PixelSize.Height;
                         
                         double angle = 0;
                         if (orientation == 6) angle = 90;
                         else if (orientation == 8) angle = -90; // 270
                         else if (orientation == 3) angle = 180;
                         
                         var newW = (orientation == 6 || orientation == 8) ? h : w;
                         var newH = (orientation == 6 || orientation == 8) ? w : h;
                         
                         await Dispatcher.UIThread.InvokeAsync(() =>
                         {
                             var rtb = new RenderTargetBitmap(new Avalonia.PixelSize(newW, newH));
                             using (var ctx = rtb.CreateDrawingContext())
                             {
                                  var matrix = Avalonia.Matrix.CreateTranslation(-w/2.0, -h/2.0) * 
                                               Avalonia.Matrix.CreateRotation(System.Math.PI * angle / 180.0) *
                                               Avalonia.Matrix.CreateTranslation(newW/2.0, newH/2.0);
                                               
                                  using (ctx.PushTransform(matrix))
                                  {
                                      ctx.DrawImage(finalBitmap, new Avalonia.Rect(0, 0, w, h));
                                  }
                             }
                             // Replace with rotated version
                             var old = finalBitmap;
                             finalBitmap = rtb;
                             old.Dispose(); // Dispose unrotated optimized bitmap
                         });
                    }
                    
                    // Assign to property
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_isDisposed) Thumbnail = finalBitmap;
                        _isLoadingThumbnail = false;
                    });
                }
                catch
                {
                    _isLoadingThumbnail = false;
                }
            });
        }
        catch
        {
            _isLoadingThumbnail = false;
        }
        finally
        {
            _thumbnailSemaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        ParentItem.PropertyChanged -= OnParentPropertyChanged;
    }
}
