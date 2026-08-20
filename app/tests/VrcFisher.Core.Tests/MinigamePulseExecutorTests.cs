using System.Diagnostics;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Input;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class MinigamePulseExecutorTests
{
    [Fact]
    public async Task Planned_release_uses_dynamic_deadline_and_always_releases()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);

        var result = await executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(30),
            CancellationToken.None);

        Assert.True(result.Input.Succeeded);
        Assert.Equal("Planned", result.ReleaseCause);
        Assert.InRange(result.ActualHold.TotalMilliseconds, 25, 150);
        Assert.Equal(["press", "release"], input.Events);
    }

    [Fact]
    public async Task Cancellation_during_pulse_releases_before_returning()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(null, TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();

        var pulse = executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(20),
            control,
            cancellation.Token);
        await input.Pressed.Task;
        cancellation.Cancel();
        var result = await pulse;

        Assert.True(result.Canceled);
        Assert.True(result.Input.Succeeded);
        Assert.Equal(["press", "release"], input.Events);
    }

    [Fact]
    public async Task Concurrent_pulses_are_serialized()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);

        var first = executor.ExecuteAsync(TimeSpan.FromMilliseconds(35), CancellationToken.None);
        await input.Pressed.Task;
        var second = executor.ExecuteAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None);
        await Task.Delay(10);

        Assert.Equal(["press"], input.Events);
        await Task.WhenAll(first, second);
        Assert.Equal(["press", "release", "press", "release"], input.Events);
    }

    [Fact]
    public async Task New_feedback_can_extend_the_planned_release()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(400));
        var timer = Stopwatch.StartNew();

        var pulse = executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(10),
            control,
            CancellationToken.None);
        await input.Pressed.Task;
        control.ScheduleAfter(TimeSpan.FromMilliseconds(140));
        await Task.Delay(70);

        Assert.Equal(["press"], input.Events);
        var result = await pulse;

        Assert.True(result.Input.Succeeded);
        Assert.Equal("Planned", result.ReleaseCause);
        Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(120));
    }

    [Fact]
    public async Task Replanned_release_is_compared_with_its_absolute_deadline()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(2));

        var pulse = executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(10),
            control,
            CancellationToken.None);
        await input.Pressed.Task;
        await Task.Delay(90);
        control.ScheduleAfter(TimeSpan.FromMilliseconds(20));
        var result = await pulse;

        Assert.True(result.ActualHold > TimeSpan.FromMilliseconds(80));
        Assert.False(result.TimingOverrun);
        Assert.True(result.ReleaseLateness < MinigamePulseExecutor.TimingOverrunTolerance);
    }

    [Fact]
    public async Task Release_request_waits_for_minimum_hold_then_releases()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2));

        var pulse = executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(45),
            control,
            CancellationToken.None);
        await input.Pressed.Task;
        control.RequestRelease();
        await Task.Delay(15);
        Assert.Equal(["press"], input.Events);

        var result = await pulse;

        Assert.True(result.Input.Succeeded);
        Assert.True(result.ReleaseRequested);
        Assert.Equal("Requested", result.ReleaseCause);
        Assert.True(result.ActualHold >= TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public async Task Feedback_updates_prevent_watchdog_release()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(null, TimeSpan.FromMilliseconds(45));

        var pulse = executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(10),
            control,
            CancellationToken.None);
        await input.Pressed.Task;
        for (var index = 0; index < 3; index++)
        {
            await Task.Delay(25);
            control.Touch();
        }

        Assert.Equal(["press"], input.Events);
        control.RequestRelease();
        var result = await pulse;

        Assert.False(result.EmergencyReleased);
        Assert.Equal("Requested", result.ReleaseCause);
    }

    [Fact]
    public async Task Missing_feedback_uses_independent_watchdog()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(null, TimeSpan.FromMilliseconds(40));

        var result = await executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(10),
            control,
            CancellationToken.None);

        Assert.True(result.Input.Succeeded);
        Assert.True(result.EmergencyReleased);
        Assert.Equal("FeedbackTimeout", result.ReleaseCause);
        Assert.Equal(["press", "release"], input.Events);
    }

    [Fact]
    public async Task Bounded_plan_can_release_repress_and_release_at_horizon()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromMilliseconds(160),
            TimeSpan.FromMilliseconds(400));

        var result = await executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(20),
            control,
            CancellationToken.None);

        Assert.True(result.Input.Succeeded);
        Assert.Equal("Planned", result.ReleaseCause);
        Assert.Equal(["press", "release", "press", "release"], input.Events);

        var events = input.TimedEvents;
        var repressDelay = Stopwatch.GetElapsedTime(events[0].Timestamp, events[2].Timestamp);
        var planDuration = Stopwatch.GetElapsedTime(events[0].Timestamp, events[3].Timestamp);
        Assert.True(repressDelay >= TimeSpan.FromMilliseconds(65));
        Assert.InRange(planDuration.TotalMilliseconds, 135, 280);
    }

    [Fact]
    public async Task Fresh_plan_can_cancel_a_pending_repress()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(input);
        var control = new PulseReleaseControl(
            TimeSpan.FromMilliseconds(25),
            TimeSpan.FromMilliseconds(120),
            TimeSpan.FromMilliseconds(220),
            TimeSpan.FromMilliseconds(400));

        var execution = executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(20),
            control,
            CancellationToken.None);
        await input.Released.Task;
        control.UpdatePlan(
            pressNow: false,
            releaseDelay: null,
            repressDelay: null,
            planHorizon: TimeSpan.FromMilliseconds(100),
            feedbackTimeout: TimeSpan.FromMilliseconds(400));
        var result = await execution;

        Assert.True(result.Input.Succeeded);
        Assert.Equal("Planned", result.ReleaseCause);
        Assert.Equal(["press", "release"], input.Events);
    }

    [Fact]
    public void Input_timeline_reports_state_at_capture_and_later_transitions()
    {
        var control = new PulseReleaseControl(null, TimeSpan.FromSeconds(1));

        control.Begin(TimeSpan.FromMilliseconds(10));
        control.MarkReleased();
        control.MarkPressed();

        var fullTimeline = control.InputTimeline(TimeSpan.Zero, TimeSpan.MaxValue);
        Assert.Equal(MinigameInputState.Released, fullTimeline.InitialState);
        Assert.Equal(
            [
                MinigameInputState.Pressed,
                MinigameInputState.Released,
                MinigameInputState.Pressed
            ],
            fullTimeline.Transitions.Select(transition => transition.State));

        var afterFirstPress = control.InputTimeline(
            fullTimeline.Transitions[0].Timestamp,
            TimeSpan.MaxValue);
        Assert.Equal(MinigameInputState.Pressed, afterFirstPress.InitialState);
        Assert.Equal(
            [MinigameInputState.Released, MinigameInputState.Pressed],
            afterFirstPress.Transitions.Select(transition => transition.State));
    }

    [Fact]
    public async Task Actual_timing_overrun_is_reported_without_input_failure()
    {
        var input = new RecordingInputController();
        var executor = new MinigamePulseExecutor(
            input,
            (_, _) =>
            {
                Thread.Sleep(70);
                return Task.CompletedTask;
            });

        var result = await executor.ExecuteAsync(
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.True(result.Input.Succeeded);
        Assert.True(result.TimingOverrun);
        Assert.False(result.EmergencyReleased);
        Assert.Equal(["press", "release"], input.Events);
    }

    private sealed class RecordingInputController : IInputController
    {
        private readonly object _sync = new();
        private readonly List<string> _events = [];
        private readonly List<TimedInputEvent> _timedEvents = [];
        private bool _held;

        public bool IsTargetForeground => true;
        public TaskCompletionSource Pressed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> Events
        {
            get { lock (_sync) return _events.ToArray(); }
        }
        public IReadOnlyList<TimedInputEvent> TimedEvents
        {
            get { lock (_sync) return _timedEvents.ToArray(); }
        }

        public InputExecutionResult Click() => InputExecutionResult.NoChange;

        public InputExecutionResult PressLeft()
        {
            lock (_sync)
            {
                if (_held) return InputExecutionResult.NoChange;
                _held = true;
                _events.Add("press");
                _timedEvents.Add(new("press", Stopwatch.GetTimestamp()));
                Pressed.TrySetResult();
                return InputExecutionResult.Success(1, 1);
            }
        }

        public InputExecutionResult ReleaseLeft()
        {
            lock (_sync)
            {
                if (!_held) return InputExecutionResult.NoChange;
                _held = false;
                _events.Add("release");
                _timedEvents.Add(new("release", Stopwatch.GetTimestamp()));
                Released.TrySetResult();
                return InputExecutionResult.Success(1, 1);
            }
        }

        public InputExecutionResult ReleaseAll() => ReleaseLeft();
    }

    private readonly record struct TimedInputEvent(string Name, long Timestamp);
}
