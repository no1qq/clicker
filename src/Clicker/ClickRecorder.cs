using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace zolsi.cc;

internal sealed class ClickRecorder
{
    private readonly Stopwatch _sw = new();
    private readonly List<ClickTiming> _timings = [];
    private readonly Queue<long> _recentClicks = new();
    private long _firstDownMs = -1;
    private long _lastDownMs = -1;
    private long _lastUpMs = -1;
    private int _lastDownX;
    private int _lastDownY;

    public bool IsRecording { get; private set; }
    public int TotalClicks => _timings.Count;

    public void Start()
    {
        _sw.Restart();
        _timings.Clear();
        _recentClicks.Clear();
        _firstDownMs = -1;
        _lastDownMs = -1;
        _lastUpMs = -1;
        _lastDownX = 0;
        _lastDownY = 0;
        IsRecording = true;
    }

    public bool ShouldAutoFinish()
    {
        if (!IsRecording || _timings.Count == 0)
        {
            return false;
        }

        long now = _sw.ElapsedMilliseconds;
        if (_lastDownMs < 0 && _lastUpMs > 0 && (now - _lastUpMs >= 500))
        {
            return true;
        }

        if (_lastDownMs > 0 && (now - _lastDownMs >= 500))
        {
            return true;
        }

        return false;
    }

    public void OnMouseDown(int x = 0, int y = 0)
    {
        if (!IsRecording)
        {
            return;
        }

        long now = _sw.ElapsedMilliseconds;

        if (_firstDownMs < 0)
        {
            _firstDownMs = now;
        }

        if (_lastUpMs > 0 && _timings.Count > 0 && (now - _lastUpMs >= 500))
        {
            return;
        }

        if (_lastDownMs > 0 && _lastUpMs < _lastDownMs)
        {
            int diff = Math.Max(20, (int)(now - _lastDownMs));
            int hold = Math.Clamp(diff / 2, 15, 60);
            int gap = Math.Max(10, diff - hold);
            if (gap < 500)
            {
                _timings.Add(new ClickTiming(hold, gap, _lastDownX, _lastDownY));
            }
        }

        _lastDownX = x;
        _lastDownY = y;
        _lastDownMs = now;
        _recentClicks.Enqueue(now);
    }

    public void OnMouseUp()
    {
        if (!IsRecording || _lastDownMs < 0)
        {
            return;
        }

        long now = _sw.ElapsedMilliseconds;
        int hold = Math.Max(10, (int)(now - _lastDownMs));
        int gap = _lastUpMs > 0 ? Math.Max(10, (int)(_lastDownMs - _lastUpMs)) : 40;

        if (gap < 500)
        {
            _timings.Add(new ClickTiming(hold, gap, _lastDownX, _lastDownY));
            _lastUpMs = now;
        }
        _lastDownMs = -1;
    }

    public double GetLiveCps()
    {
        if (!IsRecording)
        {
            return 0.0;
        }

        long now = _sw.ElapsedMilliseconds;
        while (_recentClicks.Count > 0 && (now - _recentClicks.Peek()) > 1000)
        {
            _recentClicks.Dequeue();
        }

        if (_recentClicks.Count == 0)
        {
            return 0.0;
        }

        return Math.Round((double)_recentClicks.Count, 1);
    }

    public (ClickPattern Pattern, double FinalCps, double DurationSeconds) Finish()
    {
        IsRecording = false;
        _sw.Stop();

        if (_lastDownMs > 0 && _lastUpMs < _lastDownMs)
        {
            long now = _lastDownMs + 30;
            int hold = 30;
            int gap = _lastUpMs > 0 ? Math.Max(10, (int)(_lastDownMs - _lastUpMs)) : 40;
            if (gap < 500)
            {
                _timings.Add(new ClickTiming(hold, gap, _lastDownX, _lastDownY));
                _lastUpMs = now;
            }
            _lastDownMs = -1;
        }

        double durationSec;
        if (_timings.Count > 0 && _lastUpMs > _firstDownMs)
        {
            durationSec = Math.Max(0.1, (_lastUpMs - _firstDownMs) / 1000.0);
        }
        else
        {
            durationSec = Math.Max(0.1, _sw.Elapsed.TotalSeconds);
        }

        double finalCps = _timings.Count > 0 ? Math.Round(_timings.Count / durationSec, 1) : 0.0;

        var pattern = new ClickPattern(new List<ClickTiming>(_timings), finalCps);
        return (pattern, finalCps, durationSec);
    }
}