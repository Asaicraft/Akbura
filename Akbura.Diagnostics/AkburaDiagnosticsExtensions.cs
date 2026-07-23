using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace Akbura.Diagnostics;

public static class AkburaDiagnosticsExtensions
{
    private static readonly object s_gate = new();
    private static readonly ConditionalWeakTable<Application, DiagnosticsAttachment> s_attachments = new();

    public static void AttachAkburaDevTools(this Application application)
    {
        AttachAkburaDevTools(application, new KeyGesture(Key.F12));
    }

    public static void AttachAkburaDevTools(
        this Application application,
        KeyGesture toggleGesture)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(toggleGesture);

        lock (s_gate)
        {
            if (s_attachments.TryGetValue(application, out var existing))
            {
                if (!DiagnosticsAttachment.HasSameGesture(existing.ToggleGesture, toggleGesture))
                {
                    throw new InvalidOperationException(
                        "Akbura diagnostics has already been attached with a different key gesture.");
                }

                return;
            }

            var attachment = new DiagnosticsAttachment(application, toggleGesture);
            s_attachments.Add(application, attachment);
            attachment.ScheduleInitialization();
        }
    }

    internal static bool HasSameToggleGesture(KeyGesture left, KeyGesture right)
    {
        return DiagnosticsAttachment.HasSameGesture(left, right);
    }

    private sealed class DiagnosticsAttachment
    {
        private const KeyModifiers KeyboardModifierMask =
            KeyModifiers.Control |
            KeyModifiers.Shift |
            KeyModifiers.Alt |
            KeyModifiers.Meta;

        private readonly Application _application;
        private IDisposable? _keyDownSubscription;
        private IDisposable? _keyUpSubscription;
        private readonly KeyGestureLatch _gestureLatch;
        private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
        private DiagnosticsWindow? _window;

        public DiagnosticsAttachment(Application application, KeyGesture toggleGesture)
        {
            _application = application;
            ToggleGesture = toggleGesture;
            _gestureLatch = new KeyGestureLatch(toggleGesture);
        }

        public KeyGesture ToggleGesture { get; }

        public static bool HasSameGesture(KeyGesture left, KeyGesture right)
        {
            return left.Key == right.Key &&
                (left.KeyModifiers & KeyboardModifierMask) ==
                (right.KeyModifiers & KeyboardModifierMask);
        }

        public void ScheduleInitialization()
        {
            Dispatcher.UIThread.Post(Initialize);
        }

        private void OnKeyDown(Window window, KeyEventArgs eventArgs)
        {
            var shouldToggle = _gestureLatch.Press(
                eventArgs.Key,
                eventArgs.KeyModifiers,
                out var handled);
            if (handled)
            {
                eventArgs.Handled = true;
            }

            if (!shouldToggle)
            {
                return;
            }

            ToggleWindow();
        }

        private void OnKeyUp(Window window, KeyEventArgs eventArgs)
        {
            if (_gestureLatch.Release(eventArgs.Key))
            {
                eventArgs.Handled = true;
            }
        }

        private void Initialize()
        {
            if (_application.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                throw new NotSupportedException(
                    "Akbura diagnostics currently supports only the classic desktop application lifetime.");
            }

            if (_desktopLifetime != null)
            {
                return;
            }

            _desktopLifetime = desktop;
            desktop.Exit += OnApplicationExit;

            _keyDownSubscription ??= InputElement.KeyDownEvent.AddClassHandler<Window>(
                OnKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _keyUpSubscription ??= InputElement.KeyUpEvent.AddClassHandler<Window>(
                OnKeyUp,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
        }

        private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
        {
            if (_desktopLifetime != null)
            {
                _desktopLifetime.Exit -= OnApplicationExit;
                _desktopLifetime = null;
            }

            _keyDownSubscription?.Dispose();
            _keyDownSubscription = null;
            _keyUpSubscription?.Dispose();
            _keyUpSubscription = null;
            _window?.Close();
            _window = null;
        }

        private void ToggleWindow()
        {
            if (_desktopLifetime is not { } desktop)
            {
                throw new NotSupportedException(
                    "Akbura diagnostics currently supports only the classic desktop application lifetime.");
            }

            if (_window is { IsVisible: true })
            {
                _window.Close();
                return;
            }

            var window = new DiagnosticsWindow();
            _window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
            };

            if (desktop.MainWindow is { IsVisible: true } owner)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }
        }

    }

    internal sealed class KeyGestureLatch
    {
        private const KeyModifiers KeyboardModifierMask =
            KeyModifiers.Control |
            KeyModifiers.Shift |
            KeyModifiers.Alt |
            KeyModifiers.Meta;

        private readonly KeyGesture _gesture;
        private bool _isKeyDown;
        private bool _isRecognized;

        public KeyGestureLatch(KeyGesture gesture)
        {
            _gesture = gesture;
        }

        public bool Press(Key key, KeyModifiers modifiers, out bool handled)
        {
            handled = false;
            if (key != _gesture.Key)
            {
                return false;
            }

            if (_isKeyDown)
            {
                handled = _isRecognized;
                return false;
            }

            _isKeyDown = true;
            _isRecognized =
                (modifiers & KeyboardModifierMask) ==
                (_gesture.KeyModifiers & KeyboardModifierMask);
            handled = _isRecognized;
            return _isRecognized;
        }

        public bool Release(Key key)
        {
            if (key != _gesture.Key || !_isKeyDown)
            {
                return false;
            }

            var handled = _isRecognized;
            _isKeyDown = false;
            _isRecognized = false;
            return handled;
        }
    }
}
