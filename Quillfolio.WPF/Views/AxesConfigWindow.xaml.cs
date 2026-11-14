using System;
using System.Windows;
using Quillfolio.Core.Models;
using Quillfolio.WPF.Services;

namespace Quillfolio.WPF.Views;

public partial class AxesConfigWindow : Window
{
    public CoordinateAxesOptions Options { get; private set; } = new();
    public double AxesSize { get; private set; } = 360.0;

    public AxesConfigWindow()
    {
        InitializeComponent();
        WindowBackdropHelper.ApplyModernWindowChrome(this);
        UIAnimationHelper.AttachWindowEntranceAnimation(this);
        UpdateLabels();
    }

    public AxesConfigWindow(CoordinateAxesOptions existing, double existingSize, bool isEdit = true) : this()
    {
        SliderMinX.Value = Math.Clamp(existing.MinX, -30, -1);
        SliderMaxX.Value = Math.Clamp(existing.MaxX, 1, 30);
        SliderMinY.Value = Math.Clamp(existing.MinY, -30, -1);
        SliderMaxY.Value = Math.Clamp(existing.MaxY, 1, 30);
        SliderStep.Value = Math.Clamp(existing.Step, 1, 10);
        SliderSize.Value = Math.Clamp(existingSize, 160, 1000);
        ChkShowXAxis.IsChecked = existing.ShowXAxis;
        ChkShowYAxis.IsChecked = existing.ShowYAxis;
        ChkShowLabels.IsChecked = existing.ShowLabels;
        if (isEdit)
        {
            TxtHeaderTitle.Text = Quillfolio.Core.Services.LocalizationService.Instance.Get("AxesEditTitle");
            BtnInsert.Content = Quillfolio.Core.Services.LocalizationService.Instance.Get("Apply");
        }
        UpdateLabels();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        if (!IsInitialized ||
            ValMinX == null || ValMaxX == null || ValMinY == null || ValMaxY == null ||
            ValStep == null || ValSize == null ||
            SliderMinX == null || SliderMaxX == null || SliderMinY == null || SliderMaxY == null ||
            SliderStep == null || SliderSize == null)
        {
            return;
        }

        ValMinX.Text = ((int)SliderMinX.Value).ToString();
        ValMaxX.Text = ((int)SliderMaxX.Value).ToString();
        ValMinY.Text = ((int)SliderMinY.Value).ToString();
        ValMaxY.Text = ((int)SliderMaxY.Value).ToString();
        ValStep.Text = ((int)SliderStep.Value).ToString();
        ValSize.Text = $"{(int)SliderSize.Value} px";
    }

    private void BtnInsert_Click(object sender, RoutedEventArgs e)
    {
        int minX = (int)SliderMinX.Value;
        int maxX = (int)SliderMaxX.Value;
        int minY = (int)SliderMinY.Value;
        int maxY = (int)SliderMaxY.Value;
        int step = Math.Max(1, (int)SliderStep.Value);
        double size = Math.Clamp(SliderSize.Value, 160, 1200);

        if (maxX <= minX) maxX = minX + 2;
        if (maxY <= minY) maxY = minY + 2;

        bool showX = ChkShowXAxis.IsChecked == true;
        bool showY = ChkShowYAxis.IsChecked == true;
        if (!showX && !showY) showX = true;

        Options = new CoordinateAxesOptions
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            Step = step,
            ShowLabels = ChkShowLabels.IsChecked == true,
            ShowGrid = false,
            ShowXAxis = showX,
            ShowYAxis = showY
        };
        AxesSize = size;
        DialogResult = true;
        Close();
    }
}
