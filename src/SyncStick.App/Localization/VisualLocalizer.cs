using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SyncStick.App.Localization;

/// <summary>
/// Translates the text of a window's visual tree at runtime.  Static XAML copy,
/// template content and bound values (counters, status lines, activity rows) are
/// all handled the same way: after every layout pass the leaves are read, passed
/// through <see cref="Loc.T"/> and written back with SetCurrentValue, which keeps
/// existing bindings alive.  Translation is idempotent, so the Chinese text that a
/// binding re-applies later is translated again on the next pass.
/// </summary>
public static class VisualLocalizer
{
    private const int DebounceMilliseconds = 120;

    private static readonly Regex CjkPattern = new("[\u4e00-\u9fff]", RegexOptions.Compiled);
    private static readonly HashSet<Window> Attached = new();
    private static readonly ConditionalWeakTable<Window, DispatcherTimer> Timers = new();

    /// <summary>
    /// Starts localizing <paramref name="window"/>.  Does nothing when the UI is
    /// Chinese, because the source copy is already Chinese.
    /// </summary>
    public static void Attach(Window window)
    {
        if (!Loc.IsEnglish || window is null || !Attached.Add(window))
        {
            return;
        }

        window.Loaded += (_, _) => Schedule(window);
        window.LayoutUpdated += (_, _) => Schedule(window);
        window.Closed += (_, _) => Attached.Remove(window);

        if (window.IsLoaded)
        {
            Schedule(window);
        }
    }

    /// <summary>Translates the tree immediately (used after a page switch or a dialog opens).</summary>
    public static void TranslateNow(Window window)
    {
        if (!Loc.IsEnglish || window is null)
        {
            return;
        }

        try
        {
            TranslateElement(window);
            TranslateChildren(window);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Never let localization faults reach the dispatcher: the UI keeps
            // its original text and the next pass tries again.
        }
    }

    private static void Schedule(Window window)
    {
        var timer = Timers.GetValue(window, static w =>
        {
            var debounce = new DispatcherTimer(DispatcherPriority.Background, w.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds)
            };
            return debounce;
        });

        timer.Stop();
        timer.Start();

        if (!Timers.TryGetValue(window, out var registered))
        {
            return;
        }

        // The Tick handler is attached once; guard with the Tag so re-scheduling is cheap.
        if (window.Tag is not true)
        {
            window.Tag = true;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                TranslateNow(window);
            };
        }
    }

    private static void TranslateChildren(DependencyObject parent)
    {
        // Take a snapshot before writing any text: applying a translation can
        // realize template content or regenerate item containers, which must
        // never mutate the tree while it is being walked.
        var children = new List<DependencyObject>();
        try
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                try
                {
                    children.Add(VisualTreeHelper.GetChild(parent, index));
                }
                catch (ArgumentOutOfRangeException)
                {
                    // The tree changed while it was read; the next pass catches up.
                }
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var child in children)
        {
            if (child is TextBoxBase or PasswordBox)
            {
                // User input: never translate what the user typed.
                continue;
            }

            try
            {
                TranslateElement(child);
                TranslateChildren(child);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // A virtualized / late-realized subtree may change mid-walk.
                // Translation is best-effort and retried on the next pass.
            }
        }
    }

    private static void TranslateElement(DependencyObject element)
    {
        switch (element)
        {
            case TextBlock textBlock:
                TranslateTextBlock(textBlock);
                break;

            case ContentControl contentControl when contentControl.Content is string content:
                Apply(contentControl, ContentControl.ContentProperty, content);
                break;
        }

        if (element is FrameworkElement frameworkElement)
        {
            if (frameworkElement.ToolTip is string toolTip)
            {
                Apply(frameworkElement, FrameworkElement.ToolTipProperty, toolTip);
            }

            var name = AutomationProperties.GetName(frameworkElement);
            if (!string.IsNullOrEmpty(name))
            {
                var translatedName = Loc.T(name);
                if (!string.Equals(name, translatedName, StringComparison.Ordinal))
                {
                    AutomationProperties.SetName(frameworkElement, translatedName);
                }
            }
        }

        if (element is Window window)
        {
            Apply(window, Window.TitleProperty, window.Title);
        }
    }

    private static void TranslateTextBlock(TextBlock textBlock)
    {
        if (textBlock.Inlines.Count == 0)
        {
            Apply(textBlock, TextBlock.TextProperty, textBlock.Text);
            return;
        }

        var runs = new List<Run>();
        foreach (var inline in textBlock.Inlines)
        {
            if (inline is Run run)
            {
                runs.Add(run);
            }
        }

        foreach (var run in runs)
        {
            Apply(run, Run.TextProperty, run.Text);
        }
    }

    private static void Apply(DependencyObject target, DependencyProperty property, string? source)
    {
        if (string.IsNullOrEmpty(source) || !CjkPattern.IsMatch(source))
        {
            return;
        }

        var translated = Loc.T(source);
        if (!string.Equals(source, translated, StringComparison.Ordinal))
        {
            target.SetCurrentValue(property, translated);
        }
    }
}
