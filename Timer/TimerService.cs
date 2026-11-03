using System;
using Avalonia.Threading;

namespace ducky;

public class TimerService
{
    private readonly DispatcherTimer updateTimer;

    private DateTime endTime;
    private DateTime stopwatchStart;
    private TimeSpan remainingTime;
    private TimeSpan stopwatchElapsed;

    public event Action? CountdownCompleted;

    public TimerState State { get; private set; } = new();

    public event Action<TimerState>? StateChanged;

    public TimerService()
    {
        updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };

        updateTimer.Tick += UpdateTimer;
    }

    public void StartCountdown(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        remainingTime = duration;
        endTime = DateTime.Now.Add(duration);

        State = new TimerState
        {
            Mode = TimerMode.Countdown,
            Time = duration,
            IsRunning = true,
            IsPaused = false
        };

        updateTimer.Start();
        Notify();
    }

    public void StartStopwatch()
    {
        stopwatchElapsed = TimeSpan.Zero;
        stopwatchStart = DateTime.Now;

        State = new TimerState
        {
            Mode = TimerMode.Stopwatch,
            Time = TimeSpan.Zero,
            IsRunning = true,
            IsPaused = false
        };

        updateTimer.Start();
        Notify();
    }

    public void Pause()
    {
        if (!State.IsRunning)
            return;

        if (State.Mode == TimerMode.Countdown)
        {
            remainingTime = endTime - DateTime.Now;

            if (remainingTime < TimeSpan.Zero)
                remainingTime = TimeSpan.Zero;
        }
        else if (State.Mode == TimerMode.Stopwatch)
        {
            stopwatchElapsed += DateTime.Now - stopwatchStart;
        }

        updateTimer.Stop();

        State.IsRunning = false;
        State.IsPaused = true;

        if (State.Mode == TimerMode.Countdown)
            State.Time = remainingTime;

        if (State.Mode == TimerMode.Stopwatch)
            State.Time = stopwatchElapsed;

        Notify();
    }

    public void Resume()
    {
        if (!State.IsPaused)
            return;

        if (State.Mode == TimerMode.Countdown)
        {
            endTime = DateTime.Now.Add(remainingTime);
        }
        else if (State.Mode == TimerMode.Stopwatch)
        {
            stopwatchStart = DateTime.Now;
        }

        State.IsRunning = true;
        State.IsPaused = false;

        updateTimer.Start();
        Notify();
    }

    public void Reset(TimeSpan defaultCountdownTime)
    {
        updateTimer.Stop();
    
        remainingTime = TimeSpan.Zero;
        stopwatchElapsed = TimeSpan.Zero;
    
        TimeSpan resetTime = TimeSpan.Zero;
    
        if(State.Mode == TimerMode.Countdown)
        {
            resetTime = defaultCountdownTime;
            remainingTime = defaultCountdownTime;
        }
    
        State = new TimerState
        {
            Mode = State.Mode,
            Time = resetTime,
            IsRunning = false,
            IsPaused = false
        };
    
        Notify();
    }

    public void Stop()
    {
        Reset(TimeSpan.Zero);
    }

    private void UpdateTimer(object? sender, EventArgs e)
    {
        if (!State.IsRunning)
            return;

        if (State.Mode == TimerMode.Countdown)
        {
            remainingTime = endTime - DateTime.Now;

            if (remainingTime <= TimeSpan.Zero)
            {
                remainingTime = TimeSpan.Zero;

                State.Time = TimeSpan.Zero;
                State.IsRunning = false;
                State.IsPaused = false;

                updateTimer.Stop();

                Notify();
                CountdownCompleted?.Invoke();
                return;
            }

            State.Time = remainingTime;
        }
        else if (State.Mode == TimerMode.Stopwatch)
        {
            State.Time = stopwatchElapsed + (DateTime.Now - stopwatchStart);
        }

        Notify();
    }

    private void Notify()
    {
        StateChanged?.Invoke(State);
    }
}