using System;
using System.Collections.Generic;

namespace SamProduction
{
    public enum SamEventKind { Effect, StopEffects, Hit, Takeoff, Land, Grip, Ignition, Release, Recover, AudioCue }

    [Serializable]
    public sealed class SamTimedEvent
    {
        public string id;
        public double time;
        public SamEventKind kind;
        public string socket;
        public string effect;
        public float value = 1f;
    }

    // The same clock drives gameplay, review capture, and optional cinematics.
    // Event cursor is monotonic for one execution, including large frame steps.
    public sealed class SamSkillClock
    {
        private SamTimedEvent[] events = Array.Empty<SamTimedEvent>();
        private int cursor;
        public double Time { get; private set; }
        public double Duration { get; private set; }
        public bool Running { get; private set; }
        private bool paused;
        public bool Paused
        {
            get => paused;
            set { if (paused == value) return; paused = value; PauseChanged?.Invoke(value); }
        }
        internal event Action<bool> PauseChanged;
        public long ExecutionId { get; private set; }
        public event Action<SamTimedEvent, long> Fired;
        public event Action<bool, long> Finished;

        public void Begin(double duration, IReadOnlyList<SamTimedEvent> source)
        {
            if (!Finite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
            var validated = new SamTimedEvent[source?.Count ?? 0];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < validated.Length; i++)
            {
                var e = source[i];
                if (e == null || string.IsNullOrEmpty(e.id) || !ids.Add(e.id) ||
                    !Finite(e.time) || e.time < 0 || e.time > duration)
                    throw new ArgumentException("Each event requires a unique ID and a finite time inside the skill.", nameof(source));
                if (i > 0 && e.time < source[i - 1].time)
                    throw new ArgumentException("Events must be in stable chronological order.", nameof(source));
                validated[i] = new SamTimedEvent { id = e.id, time = e.time, kind = e.kind,
                    socket = e.socket, effect = e.effect, value = e.value };
            }
            long previousExecution = ExecutionId;
            Cancel();
            // A Finished listener may have started a newer request. Latest request wins.
            if (ExecutionId != previousExecution) return;
            events = validated;
            cursor = 0;
            Duration = duration;
            Time = 0;
            Paused = false;
            Running = true;
            ExecutionId++;
            Dispatch();
        }

        public void Advance(double delta, double speed = 1)
        {
            if (!Finite(delta) || delta < 0 || !Finite(speed) || speed < 0 || !Finite(delta * speed))
                throw new ArgumentOutOfRangeException(nameof(delta));
            if (!Running || Paused) return;
            Time = Math.Min(Duration, Time + delta * speed);
            if (Duration - Time < 1e-8) Time = Duration;
            long execution = ExecutionId;
            Dispatch();
            if (Running && execution == ExecutionId && Time >= Duration)
            {
                Running = false;
                Finished?.Invoke(false, execution);
            }
        }

        public void Cancel()
        {
            bool wasRunning = Running;
            Running = false; // Pause observers must not rebuild a cancelled execution.
            Paused = false;
            if (!wasRunning) return;
            cursor = events.Length;
            Finished?.Invoke(true, ExecutionId);
        }

        private void Dispatch()
        {
            long execution = ExecutionId;
            while (Running && ExecutionId == execution && cursor < events.Length && events[cursor].time <= Time + 1e-9)
            {
                var e = events[cursor++];
                Fired?.Invoke(e, execution);
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
