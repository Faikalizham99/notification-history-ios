using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using UIKit;
namespace NotificationHistory.Services;

internal static class InputPresentation
{
    public static void Configure()
    {
        // Theme-bound text colors are mapped again when the app's appearance changes.
        // Keep MAUI's existing Done accessory and clear button; do not add another X.
        EntryHandler.Mapper.AppendToMapping(nameof(IEntry.TextColor), (handler, _) =>
        {
            handler.PlatformView.BorderStyle = UITextBorderStyle.None;
            ApplyTheme(handler.PlatformView);
        });
        EditorHandler.Mapper.AppendToMapping(nameof(IEditor.TextColor), (handler, _) => ApplyTheme(handler.PlatformView));
    }

    private static void ApplyTheme(UIView input)
    {
        var application = Application.Current;
        var theme = application?.UserAppTheme ?? AppTheme.Unspecified;
        if (theme == AppTheme.Unspecified) theme = application?.RequestedTheme ?? AppTheme.Light;
        var dark = theme == AppTheme.Dark;
        var style = dark ? UIUserInterfaceStyle.Dark : UIUserInterfaceStyle.Light;
        var keyboardAppearance = dark ? UIKeyboardAppearance.Dark : UIKeyboardAppearance.Light;
        input.OverrideUserInterfaceStyle = style;

        UIView? accessory = null;
        Action? refreshKeyboard = null;
        if (input is UITextField field)
        {
            if (field.IsFirstResponder && field.KeyboardAppearance != keyboardAppearance) refreshKeyboard = field.ReloadInputViews;
            field.KeyboardAppearance = keyboardAppearance;
            accessory = field.InputAccessoryView;
        }
        else if (input is UITextView editor)
        {
            if (editor.IsFirstResponder && editor.KeyboardAppearance != keyboardAppearance) refreshKeyboard = editor.ReloadInputViews;
            editor.KeyboardAppearance = keyboardAppearance;
            accessory = editor.InputAccessoryView;
        }
        if (accessory is UIToolbar toolbar)
        {
            toolbar.OverrideUserInterfaceStyle = style;
            toolbar.BarStyle = dark ? UIBarStyle.Black : UIBarStyle.Default;
            toolbar.TintColor = UIColor.Label;
        }
        refreshKeyboard?.Invoke();
    }
}
