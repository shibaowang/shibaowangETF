using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class TradeLogRecord : INotifyPropertyChanged
{
    private long _id;
    private string _time = string.Empty;
    private string _strategyCode = string.Empty;
    private string? _actualCode;
    private string _action = string.Empty;
    private double _price;
    private double _quantity;
    private double _amount;
    private string? _tier;
    private string? _source;
    private double _fee;
    private string? _memo;
    private double _netCashImpact;
    private double _principal;
    private double _cashBalance;
    private double _totalAssets;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Time
    {
        get => _time;
        set => SetField(ref _time, value);
    }

    public string StrategyCode
    {
        get => _strategyCode;
        set => SetField(ref _strategyCode, value);
    }

    public string? ActualCode
    {
        get => _actualCode;
        set => SetField(ref _actualCode, value);
    }

    public string Action
    {
        get => _action;
        set => SetField(ref _action, value);
    }

    public double Price
    {
        get => _price;
        set => SetField(ref _price, value);
    }

    public double Quantity
    {
        get => _quantity;
        set => SetField(ref _quantity, value);
    }

    public double Amount
    {
        get => _amount;
        set => SetField(ref _amount, value);
    }

    public string? Tier
    {
        get => _tier;
        set => SetField(ref _tier, value);
    }

    public string? Source
    {
        get => _source;
        set => SetField(ref _source, value);
    }

    public double Fee
    {
        get => _fee;
        set => SetField(ref _fee, value);
    }

    public string? Memo
    {
        get => _memo;
        set => SetField(ref _memo, value);
    }

    public double NetCashImpact
    {
        get => _netCashImpact;
        set => SetField(ref _netCashImpact, value);
    }

    public double Principal
    {
        get => _principal;
        set => SetField(ref _principal, value);
    }

    public double CashBalance
    {
        get => _cashBalance;
        set => SetField(ref _cashBalance, value);
    }

    public double TotalAssets
    {
        get => _totalAssets;
        set => SetField(ref _totalAssets, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
