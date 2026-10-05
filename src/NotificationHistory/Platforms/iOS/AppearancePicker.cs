using CoreGraphics;
using Foundation;
using ImageIO;
using PhotosUI;
using UIKit;
using NotificationHistory.Core.Services;
namespace NotificationHistory.Services;

public sealed record CroppedIcon(byte[] Original, byte[] Cropped);
public static class AppearancePicker
{
    public static async Task<CroppedIcon?> PickIconAsync(string shape)
    {
        using var picker = new PhotoController();
        Present(picker);
        using var data = await picker.Completion.Task;
        if (data is null) return null;
        if (data.Length > 67_108_864) throw new ArgumentException("Choose a smaller photo and try again.");
        using var source = CGImageSource.FromData(data) ?? throw new ArgumentException("This photo could not be opened.");
        using var thumbnail = source.CreateThumbnail(0, new CGImageThumbnailOptions {
            MaxPixelSize = 2048, CreateThumbnailFromImageAlways = true, CreateThumbnailWithTransform = true
        }) ?? throw new ArgumentException("This photo could not be opened.");
        using var image = UIImage.FromImage(thumbnail);
        using var original = image.AsPNG() ?? throw new ArgumentException("This photo could not be prepared.");
        var cropped = await CropAsync(image, shape);
        return cropped is null ? null : new(original.ToArray(), cropped);
    }
    public static async Task<byte[]?> CropExistingAsync(byte[] original, string shape)
    {
        using var data = NSData.FromArray(original);
        using var image = UIImage.LoadFromData(data) ?? throw new ArgumentException("This photo could not be opened.");
        return await CropAsync(image, shape);
    }
    private static async Task<byte[]?> CropAsync(UIImage image, string shape)
    {
        using var crop = new CropController(image, shape);
        Present(crop); return await crop.Completion.Task;
    }
    public static void PickColor(string initial, string title, Action<string> changed)
    {
        var picker = new ColorController(initial, title, changed);
        Present(picker);
    }
    private static void Present(UIViewController controller)
    {
        var host = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("The editor is not ready. Try again.");
        controller.OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;
        host.PresentViewController(controller, true, null);
    }
    private sealed class PhotoController : PHPickerViewController
    {
        private readonly PhotoDelegate pickerDelegate;
        public TaskCompletionSource<NSData?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PhotoController() : base(new PHPickerConfiguration { Filter = PHPickerFilter.ImagesFilter, SelectionLimit = 1 })
        {
            pickerDelegate = new(Completion); Delegate = pickerDelegate;
            ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        }
    }
    private sealed class PhotoDelegate(TaskCompletionSource<NSData?> completion) : PHPickerViewControllerDelegate
    {
        private bool finished;
        public override async void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
        {
            if (finished) return; finished = true;
            var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            picker.DismissViewController(true, () => dismissed.TrySetResult());
            await dismissed.Task;
            try { completion.TrySetResult(results.Length == 0 ? null : await results[0].ItemProvider.LoadDataRepresentationAsync("public.image")); }
            catch { completion.TrySetException(new ArgumentException("This photo could not be opened. Choose another image.")); }
        }
    }
    private sealed class ColorController : UIColorPickerViewController
    {
        private readonly ColorDelegate colorDelegate;
        public ColorController(string initial, string title, Action<string> changed)
        {
            var color = Microsoft.Maui.Graphics.Color.FromArgb(initial);
            SelectedColor = UIColor.FromRGBA(color.Red, color.Green, color.Blue, 1);
            SupportsAlpha = false; Title = title;
            colorDelegate = new(changed); Delegate = colorDelegate;
        }
    }
    private sealed class ColorDelegate(Action<string> changed) : UIColorPickerViewControllerDelegate
    {
        public override void DidSelectColor(UIColorPickerViewController picker, UIColor color, bool continuously)
        {
            color.GetRGBA(out var red, out var green, out var blue, out _);
            changed($"#{(int)Math.Round((double)red * 255):X2}{(int)Math.Round((double)green * 255):X2}{(int)Math.Round((double)blue * 255):X2}");
        }
    }
    private sealed class CropController : UIViewController
    {
        private readonly UIImage image;
        private readonly string shape;
        private readonly UIScrollView scroll = new() { ShowsHorizontalScrollIndicator = false, ShowsVerticalScrollIndicator = false,
            Bounces = false, BouncesZoom = false, ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never };
        private readonly UIImageView imageView;
        private readonly ZoomDelegate zoomDelegate;
        private readonly UIButton cancel = Button("Cancel"), use = Button("Use"), reset = Button("↺  Reset");
        private readonly UILabel title = Label("Crop Icon", 18), instruction = Label("Drag to move, pinch to zoom.", 15);
        private nfloat viewport;
        private bool finished;
        public TaskCompletionSource<byte[]?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CropController(UIImage image, string shape)
        {
            this.image = image; this.shape = shape;
            ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
            imageView = new(image) { Frame = new CGRect(CGPoint.Empty, image.Size), UserInteractionEnabled = true };
            zoomDelegate = new(imageView); scroll.Delegate = zoomDelegate;
            cancel.TouchUpInside += (_, _) => Finish(null);
            reset.TouchUpInside += (_, _) => Recenter();
            use.TouchUpInside += (_, _) => Export();
        }
        public override void ViewDidLoad()
        {
            base.ViewDidLoad(); View!.BackgroundColor = UIColor.Black;
            scroll.AddSubview(imageView); scroll.ClipsToBounds = true;
            scroll.Layer.BorderWidth = 1; scroll.Layer.BorderColor = UIColor.FromWhiteAlpha(.6f, 1).CGColor;
            scroll.AccessibilityLabel = "Crop image. Drag to position and pinch to zoom.";
            instruction.TextColor = UIColor.FromWhiteAlpha(.65f, 1);
            View.AddSubviews(cancel, title, use, instruction, scroll, reset);
        }
        public override UIStatusBarStyle PreferredStatusBarStyle() => UIStatusBarStyle.LightContent;
        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            var width = View!.Bounds.Width; var top = View.SafeAreaInsets.Top + 10;
            cancel.Frame = new(20, top, 90, 46); use.Frame = new(width - 90, top, 70, 46);
            title.Frame = new(115, top, Math.Max(70, (double)width - 210), 46);
            instruction.Frame = new(20, top + 76, width - 40, 36);
            var size = (nfloat)Math.Min(360, Math.Min((double)width - 48, (double)View.Bounds.Height - top - 230));
            scroll.Frame = new((width - size) / 2, top + 130, size, size);
            scroll.Layer.CornerRadius = shape == "Circle" ? size / 2 : size * .22f;
            reset.Frame = new((width - 140) / 2, top + size + 154, 140, 48);
            if (Math.Abs((double)(viewport - size)) > .5) { viewport = size; Recenter(); }
        }
        private void Recenter()
        {
            if (viewport <= 0) return;
            var minimum = (nfloat)Math.Max((double)(viewport / image.Size.Width), (double)(viewport / image.Size.Height));
            scroll.MinimumZoomScale = .0001f; scroll.MaximumZoomScale = minimum * 6; scroll.MinimumZoomScale = minimum;
            scroll.SetZoomScale(minimum, false);
            scroll.ContentSize = new(image.Size.Width * minimum, image.Size.Height * minimum);
            scroll.SetContentOffset(new(Math.Max(0, (double)(scroll.ContentSize.Width - viewport) / 2),
                Math.Max(0, (double)(scroll.ContentSize.Height - viewport) / 2)), false);
        }
        private void Export()
        {
            try
            {
                var rect = IconCropGeometry.DrawRectangle((double)image.Size.Width, (double)image.Size.Height,
                    (double)viewport, (double)scroll.ZoomScale, (double)scroll.ContentOffset.X, (double)scroll.ContentOffset.Y);
                using var format = new UIGraphicsImageRendererFormat { Scale = 1, Opaque = false };
                using var renderer = new UIGraphicsImageRenderer(new CGSize(512, 512), format);
                using var result = renderer.CreatePng(_ => image.Draw(new CGRect(rect.X, rect.Y, rect.Width, rect.Height)));
                Finish(result.ToArray());
            }
            catch
            {
                if (finished) return; finished = true;
                DismissViewController(true, () => Completion.TrySetException(new ArgumentException("This crop could not be prepared. Try another image.")));
            }
        }
        private void Finish(byte[]? result)
        {
            if (finished) return; finished = true;
            DismissViewController(true, () => Completion.TrySetResult(result));
        }
        private static UIButton Button(string text)
        {
            var button = new UIButton(UIButtonType.System);
            button.SetTitle(text, UIControlState.Normal); button.SetTitleColor(UIColor.White, UIControlState.Normal);
            button.TitleLabel!.Font = UIFont.SystemFontOfSize(17)!;
            button.BackgroundColor = UIColor.FromWhiteAlpha(.12f, 1); button.Layer.CornerRadius = 23;
            button.AccessibilityLabel = text; return button;
        }
        private static UILabel Label(string text, nfloat size) => new() { Text = text, TextColor = UIColor.White,
            TextAlignment = UITextAlignment.Center, Font = UIFont.SystemFontOfSize(size)! };
        private sealed class ZoomDelegate(UIView image) : UIScrollViewDelegate
        { public override UIView ViewForZoomingInScrollView(UIScrollView scrollView) => image; }
    }
}
