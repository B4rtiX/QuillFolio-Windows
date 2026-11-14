using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Quillfolio.Core.Models;

public class TableCell
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("isHeader")]
    public bool IsHeader { get; set; } = false;

    [JsonPropertyName("isBold")]
    public bool IsBold { get; set; } = false;

    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "Transparent";
}

public class DynamicTable
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("rowCount")]
    public int RowCount { get; set; } = 3;

    [JsonPropertyName("colCount")]
    public int ColCount { get; set; } = 3;

    [JsonPropertyName("cellWidth")]
    public double CellWidth { get; set; } = 140.0;

    [JsonPropertyName("cellHeight")]
    public double CellHeight { get; set; } = 42.0;

    [JsonPropertyName("headerColor")]
    public string HeaderColor { get; set; } = "#1F2937"; // Deep high-contrast Slate

    [JsonPropertyName("cells")]
    public List<List<TableCell>> Cells { get; set; } = new();

    public DynamicTable(double x = 100, double y = 100, int rows = 3, int cols = 3)
    {
        X = x;
        Y = y;
        RowCount = rows;
        ColCount = cols;
        InitializeCells();
    }

    public void InitializeCells()
    {
        Cells = new List<List<TableCell>>();
        for (int r = 0; r < RowCount; r++)
        {
            var row = new List<TableCell>();
            for (int c = 0; c < ColCount; c++)
            {
                bool isHeader = (r == 0);
                row.Add(new TableCell
                {
                    IsHeader = isHeader,
                    IsBold = isHeader,
                    Text = isHeader ? $"Header {c + 1}" : $"Data {r},{c + 1}",
                    TextColor = isHeader ? "#F9FAFB" : "#111827",
                    BackgroundColor = isHeader ? HeaderColor : (r % 2 == 1 ? "#F3F4F6" : "#FFFFFF")
                });
            }
            Cells.Add(row);
        }
    }

    public void AddRow()
    {
        RowCount++;
        var newRow = new List<TableCell>();
        for (int c = 0; c < ColCount; c++)
        {
            newRow.Add(new TableCell
            {
                IsHeader = false,
                Text = "",
                TextColor = "#111827",
                BackgroundColor = (RowCount % 2 == 1 ? "#F3F4F6" : "#FFFFFF")
            });
        }
        Cells.Add(newRow);
    }

    public void RemoveRow()
    {
        if (RowCount > 1)
        {
            RowCount--;
            Cells.RemoveAt(Cells.Count - 1);
        }
    }

    public void AddColumn()
    {
        ColCount++;
        for (int r = 0; r < RowCount; r++)
        {
            bool isHeader = (r == 0);
            Cells[r].Add(new TableCell
            {
                IsHeader = isHeader,
                IsBold = isHeader,
                Text = isHeader ? $"Col {ColCount}" : "",
                TextColor = isHeader ? "#F9FAFB" : "#111827",
                BackgroundColor = isHeader ? HeaderColor : (r % 2 == 1 ? "#F3F4F6" : "#FFFFFF")
            });
        }
    }

    public void RemoveColumn()
    {
        if (ColCount > 1)
        {
            ColCount--;
            for (int r = 0; r < RowCount; r++)
            {
                if (Cells[r].Count > ColCount)
                {
                    Cells[r].RemoveAt(Cells[r].Count - 1);
                }
            }
        }
    }
}
