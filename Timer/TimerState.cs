using System;

namespace ducky;

public class TimerState
{
    public TimerMode Mode { get; set; } = TimerMode.None;

    public TimeSpan Time { get; set; } = TimeSpan.Zero;

    public bool IsRunning { get; set; }

    public bool IsPaused { get; set; }

    public bool IsActive => Mode != TimerMode.None;
}