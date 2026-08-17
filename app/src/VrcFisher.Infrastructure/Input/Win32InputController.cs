using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Input;

public sealed class Win32InputController(
    VrChatForegroundState foreground,
    ILogger<Win32InputController> logger) : IInputController
{
    private const uint InputMouse = 0;
    private const int ClickHoldMilliseconds = 50;
    private readonly object _sync = new();
    private bool _leftDown;

    public bool IsTargetForeground => foreground.IsForeground;

    public InputExecutionResult Click()
    {
        lock (_sync)
        {
            if (!IsTargetForeground)
                return InputExecutionResult.Failure(0, 2, "VRChat is not the foreground window");

            var press = Submit([CreateMouseInput(MouseEventFlags.LeftDown)]);
            if (!press.Succeeded)
            {
                logger.LogError(
                    "SendInput click left-down failed submitted={Submitted} error={Error}",
                    press.SubmittedEvents,
                    press.Error);
                return InputExecutionResult.Failure(
                    press.SubmittedEvents,
                    2,
                    $"left-down failed: {press.Error}");
            }

            _leftDown = true;
            var pressedAt = DateTimeOffset.UtcNow;
            Thread.Sleep(ClickHoldMilliseconds);
            var remainedForeground = IsTargetForeground;
            var release = ReleaseLeftCore();
            var releasedAt = DateTimeOffset.UtcNow;
            if (!release.Succeeded)
            {
                return InputExecutionResult.Failure(
                    1 + release.SubmittedEvents,
                    2,
                    $"left-up failed: {release.Error}") with
                {
                    PressedAt = pressedAt,
                    ReleasedAt = releasedAt
                };
            }
            if (!remainedForeground || !IsTargetForeground)
            {
                return InputExecutionResult.Failure(
                    2,
                    2,
                    "VRChat lost foreground while the click was held") with
                {
                    PressedAt = pressedAt,
                    ReleasedAt = releasedAt
                };
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "SendInput click pulse submitted events=2 hold_ms={HoldMilliseconds}",
                    ClickHoldMilliseconds);
            }
            return InputExecutionResult.Success(2, 2) with
            {
                PressedAt = pressedAt,
                ReleasedAt = releasedAt
            };
        }
    }

    public InputExecutionResult PressLeft()
    {
        lock (_sync)
        {
            if (_leftDown) return InputExecutionResult.NoChange;
            if (!IsTargetForeground)
                return InputExecutionResult.Failure(0, 1, "VRChat is not the foreground window");

            var result = Submit([CreateMouseInput(MouseEventFlags.LeftDown)]);
            if (!result.Succeeded)
            {
                logger.LogError("SendInput left-down failed error={Error}", result.Error);
                return result;
            }
            _leftDown = true;
            result = result with { PressedAt = DateTimeOffset.UtcNow };
            if (!IsTargetForeground)
            {
                var release = ReleaseLeftCore();
                return InputExecutionResult.Failure(
                    result.SubmittedEvents,
                    result.ExpectedEvents,
                    release.Succeeded
                        ? "VRChat lost foreground while left-down was submitted"
                        : $"VRChat lost foreground and left-up also failed: {release.Error}");
            }
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("SendInput left-down submitted");
            return result;
        }
    }

    public InputExecutionResult ReleaseLeft()
    {
        lock (_sync)
        {
            return ReleaseLeftCore();
        }
    }

    public InputExecutionResult ReleaseAll() => ReleaseLeft();

    private InputExecutionResult ReleaseLeftCore()
    {
        if (!_leftDown) return InputExecutionResult.NoChange;
        var result = Submit([CreateMouseInput(MouseEventFlags.LeftUp)]);
        if (result.Succeeded)
        {
            _leftDown = false;
            result = result with { ReleasedAt = DateTimeOffset.UtcNow };
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("SendInput left-up submitted");
        }
        else
        {
            logger.LogError("SendInput left-up failed error={Error}", result.Error);
        }
        return result;
    }

    private static InputExecutionResult Submit(Input[] inputs)
    {
        var submitted = checked((int)SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()));
        if (submitted == inputs.Length)
            return InputExecutionResult.Success(submitted, inputs.Length);

        var error = Marshal.GetLastPInvokeError();
        return InputExecutionResult.Failure(
            submitted,
            inputs.Length,
            error == 0
                ? "SendInput rejected one or more events without a Win32 error; check process integrity levels"
                : $"SendInput Win32 error {error}");
    }

    private static Input CreateMouseInput(MouseEventFlags flags) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new MouseInput { Flags = flags }
        }
    };

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public MouseEventFlags Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

}
