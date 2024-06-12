using Avalonia.Controls;
using System;

namespace Avalonia.UpDock.Controls;

public struct DropTarget : IEquatable<DropTarget>
{
    private Dock _dock;
    private bool _isDock;
    private bool _isFill;
    private bool _isTabBar;
    private int _tabIndex;

    public static DropTarget None => new();

    public static DropTarget Dock(Dock dock)
        => new() { _isDock = true, _dock = dock };
    public static DropTarget Fill
        => new() { _isFill = true };
    public static DropTarget TabBar(int tabIndex)
        => new() { _isTabBar = true, _tabIndex = tabIndex };

    public readonly bool IsNone() => !_isDock && !_isFill && !_isTabBar;

    public readonly bool IsDock(Dock dock) => IsDock(out var value) && value == dock;
    public readonly bool IsDock(out Dock dock)
    {
        dock = _dock;
        return _isDock;
    }

    public readonly bool IsFill() => _isFill;

    public readonly bool IsTabBar(int tabIndex) => IsTabBar(out var value) && value == tabIndex;
    public readonly bool IsTabBar(out int tabIndex)
    {
        tabIndex = _tabIndex;
        return _isTabBar;
    }

    public readonly bool Equals(DropTarget other)
    {
        return
            _dock == other._dock &&
            _isDock == other._isDock &&
            _isFill == other._isFill &&
            _isTabBar == other._isTabBar &&
            _tabIndex == other._tabIndex;
    }

    public static bool operator ==(DropTarget left, DropTarget right) => left.Equals(right);
    public static bool operator !=(DropTarget left, DropTarget right) => !left.Equals(right);

    public override readonly bool Equals(object? obj)
    {
        return obj is DropTarget && Equals((DropTarget)obj);
    }

    public override readonly int GetHashCode() => 0;
}
