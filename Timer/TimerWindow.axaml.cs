
using System;
using Avalonia.Controls;

namespace ducky;

public partial class TimerWindow : Window
{
    private readonly TimerService timerService;
    private TimerMode selectedMode = TimerMode.Countdown;
    private TimeSpan defaultCountdownTime;

    public TimerWindow()
        : this(new TimerService(), TimeSpan.FromMinutes(10))
    {
    }

    public TimerWindow(TimerService service, TimeSpan defaultTime)
    {
        InitializeComponent();

        timerService = service;
        defaultCountdownTime = defaultTime;

        timerService.StateChanged += UpdateTimerDisplay;

        UpdateTimerDisplay(timerService.State);

        Closed += (_, _) =>
        {
            timerService.StateChanged -= UpdateTimerDisplay;
        };
    }

    private void UpdateTimerDisplay(TimerState state)
    {
        TimeSpan time = state.Time;

        if (time.TotalHours >= 1)
            TimerDisplay.Text = time.ToString(@"hh\:mm\:ss");
        else
            TimerDisplay.Text = time.ToString(@"mm\:ss");
    }

    private void CountdownMode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        selectedMode = TimerMode.Countdown;
        DurationInput.IsVisible = true;
    }

    private void StopwatchMode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        selectedMode = TimerMode.Stopwatch;
        DurationInput.IsVisible = false;
    }

    private void StartButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if(selectedMode == TimerMode.Stopwatch)
        {
            StartTimer();
        }
        else
        {
            StartCountdownFromInput();
        }
    }

    private void StartCountdownFromInput()
    {
        if(!int.TryParse(HoursInput.Text, out int hours))
            return;

        if(!int.TryParse(MinutesInput.Text, out int minutes))
            return;

        if(!int.TryParse(SecondsInput.Text, out int seconds))
            return;

        if(hours < 0 || minutes < 0 || minutes > 59 ||
           seconds < 0 || seconds > 59)
            return;

        TimeSpan duration = new TimeSpan(hours, minutes, seconds);

        if(duration <= TimeSpan.Zero)
            return;

        MainWindow.ResetTimerDisplay();
        timerService.StartCountdown(duration);
    }

    public void StartTimer()
    {
        selectedMode = TimerMode.Stopwatch;
    
        MainWindow.ResetTimerDisplay();
        timerService.StartStopwatch();
    }
    
    public void StartCountdown()
    {
        selectedMode = TimerMode.Countdown;
    
        MainWindow.ResetTimerDisplay();
        timerService.StartCountdown(defaultCountdownTime);
    }

    private void PauseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TogglePause();
    }

    public void TogglePause()
    {
        if (timerService.State.IsPaused)
            timerService.Resume();
        else
            timerService.Pause();
    }

    private void ResetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ResetTimer();
    }

    public void ResetTimer()
    {
        timerService.Reset(defaultCountdownTime);
        MainWindow.ResetTimerDisplay();

        HoursInput.Text = "0";
        MinutesInput.Text = "10";
        SecondsInput.Text = "0";
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    public void UpdateDefaultCountdownTime(TimeSpan time)
    {
        defaultCountdownTime = time;
    }
}
