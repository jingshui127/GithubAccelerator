using System;
using Avalonia;
using Avalonia.Styling;

namespace GithubAccelerator.UI.Services;

public static class ThemeManager
{
    private static ThemeVariant _currentTheme = ThemeVariant.Light;
    private static bool _isInitialized;

    public static event Action<ThemeVariant>? ThemeChanged;

    public static ThemeVariant CurrentTheme => _currentTheme;

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        if (Application.Current != null)
        {
            _currentTheme = Application.Current.RequestedThemeVariant ?? ThemeVariant.Light;
            Application.Current.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "RequestedThemeVariant" && Application.Current != null)
                {
                    _currentTheme = Application.Current.RequestedThemeVariant ?? ThemeVariant.Light;
                }
            };
        }
    }

    public static void SetTheme(ThemeVariant theme)
    {
        if (_currentTheme == theme) return;

        _currentTheme = theme;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = theme;
        }
        ThemeChanged?.Invoke(theme);
    }

    public static void ToggleTheme()
    {
        SetTheme(_currentTheme == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light);
    }

    public static bool IsDarkMode => _currentTheme == ThemeVariant.Dark;
}
