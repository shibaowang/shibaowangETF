using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 ValidateTradeLogSchema: 校验 TradeLog 15 关键列存在且不重复。
/// </summary>
public class TradeLogSchemaValidator
{
    public struct SchemaError
    {
        public string Message;
        public string Column;
    }

    public List<SchemaError> Errors { get; } = new();

    public bool Validate(IReadOnlyList<string> headers)
    {
        Errors.Clear();
        if (headers == null || headers.Count == 0)
        {
            Errors.Add(new SchemaError { Message = "TradeLog 表头为空。", Column = "" });
            return false;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < headers.Count; i++)
        {
            string h = headers[i].Trim();
            if (string.IsNullOrEmpty(h)) continue;
            if (seen.ContainsKey(h))
            {
                Errors.Add(new SchemaError
                {
                    Message = $"【系统熔断】TradeLog 关键表头发生重复异常！冲突列: [{h}]",
                    Column = h
                });
            }
            else
            {
                seen[h] = i;
            }
        }

        foreach (string required in TradeLogEntry.RequiredHeaders)
        {
            if (!seen.ContainsKey(required))
            {
                Errors.Add(new SchemaError
                {
                    Message = $"【系统熔断】TradeLog 关键表头缺失！缺少预期列: [{required}]",
                    Column = required
                });
            }
        }

        return Errors.Count == 0;
    }

    public bool Validate(IEnumerable<TradeLogEntry> entries, out List<SchemaError> outErrors)
    {
        outErrors = new List<SchemaError>();
        var entriesList = entries?.ToList();
        if (entriesList == null || entriesList.Count == 0) return true;

        var headerSet = new HashSet<string>(TradeLogEntry.RequiredHeaders);
        // Additional structural checks on entries themselves
        foreach (var entry in entriesList)
        {
            if (string.IsNullOrWhiteSpace(entry.StrategyCode))
            {
                outErrors.Add(new SchemaError
                {
                    Message = $"第 {entry.RowIndex} 行缺少策略代码。",
                    Column = "策略代码"
                });
            }
            if (string.IsNullOrWhiteSpace(entry.Action))
            {
                outErrors.Add(new SchemaError
                {
                    Message = $"第 {entry.RowIndex} 行缺少动作。",
                    Column = "动作"
                });
            }
        }
        return outErrors.Count == 0;
    }
}
