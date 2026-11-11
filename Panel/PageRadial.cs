using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LumenLite.Panel;

/// <summary>
/// The radial menu's settings: how to open it, and what it holds now. The ring
/// is the AI Agents page and one button per RustDesk machine, so there is
/// nothing to choose; this page only shows it.
/// </summary>
internal sealed class RadialPage : Page
{
    private readonly Func<IReadOnlyList<RadialAction>> _items;
    private readonly StackPanel _list = new();
    private readonly TextBlock _count = Kit.Mono("", 10.5, false, Kit.Secondary);

    public RadialPage(PanelWindow host, Func<IReadOnlyList<RadialAction>> items, string? hotkey, Action<bool> setMiddleHold) : base(host)
    {
        _items = items;
        var intro = new StackPanel();
        intro.Children.Add(Kit.Label("Cách dùng", 13.5, true));
        intro.Children.Add(Kit.Wrap($"Giữ chuột giữa (hoặc nhấn {hotkey ?? "phím tắt"}), vòng nút hiện quanh con trỏ. Rê về phía một nút rồi thả chuột giữa, hoặc bấm vào nút. Phím 1–8 chạy nút theo thứ tự. Esc hay bấm giữa vòng để đóng.", 12));
        Children.Add(Kit.CardOf(true, intro,
            Kit.ToggleRow("Giữ chuột giữa để mở", "Bấm chuột giữa nhanh vẫn hoạt động như cũ (mở tab mới, cuộn). Tự tắt khi chơi game hay xem video toàn màn hình.", Store.S.RadialMiddleHold, setMiddleHold)));
        Children.Add(Kit.Section("Nút trên vòng", _count));
        Children.Add(Kit.CardOf(_list));
        Children.Add(Kit.Wrap("Vòng gồm trang AI Agents trên Island và từng máy RustDesk đã lưu (tối đa 7). Bấm một máy để mở màn hình của máy đó trong RustDesk.", 12));
    }

    public override string Title => "Menu vòng tròn";

    public override void Shown() => Draw();

    private void Draw()
    {
        _list.Children.Clear();
        var items = _items();
        _count.Text = $"{items.Count}/8";
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = item.Symbol
                ? new TextBlock { Text = item.Glyph, FontSize = 17, FontFamily = new FontFamily("Segoe UI Symbol"), Foreground = Kit.Text, VerticalAlignment = VerticalAlignment.Center }
                : Kit.Glyph(item.Glyph, 16, Kit.Text);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(icon);
            var label = Kit.Label($"{i + 1}. {item.Label}", 13.5, true);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            _list.Children.Add(grid);
        }
    }
}
