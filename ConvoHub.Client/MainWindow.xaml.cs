using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConvoHub.Models;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdownTableRow = Markdig.Extensions.Tables.TableRow;

namespace ConvoHub.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string ServiceUrl = "http://localhost:5025";
    private readonly HttpClient httpClient = new();
    private HubConnection? hubConnection;
    private string windowsUser = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        windowsUser = GetConfiguredUserName();
        UserLabel.Text = windowsUser;
        hubConnection = new HubConnectionBuilder().WithUrl($"{ServiceUrl}/hubs/chat", options =>
        {
            options.Headers.Add("X-Windows-User", windowsUser);
        }).WithAutomaticReconnect().Build();
        hubConnection.On<ChatMessage>("ReceiveMessage", message => Dispatcher.Invoke(() => AddMessage(message)));

        try
        {
            await hubConnection.StartAsync();
            httpClient.DefaultRequestHeaders.Add("X-Windows-User", windowsUser);
            var messages = await httpClient.GetFromJsonAsync<IReadOnlyCollection<ChatMessage>>($"{ServiceUrl}/api/chat/messages");
            if (messages is not null)
            {
                foreach (var message in messages) AddMessage(message);
            }
        }
        catch (Exception exception)
        {
            AddSystemMessage($"無法連線至服務：{exception.Message}");
        }
    }

    private static string GetConfiguredUserName()
    {
        var arguments = Environment.GetCommandLineArgs();
        const string fakeUserPrefix = "--fake-user=";
        var inlineUser = arguments.FirstOrDefault(argument => argument.StartsWith(fakeUserPrefix, StringComparison.OrdinalIgnoreCase));
        if (inlineUser is not null && inlineUser.Length > fakeUserPrefix.Length)
        {
            return inlineUser[fakeUserPrefix.Length..].Trim();
        }

        var optionIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--fake-user", StringComparison.OrdinalIgnoreCase));
        if (optionIndex >= 0 && optionIndex + 1 < arguments.Length && !arguments[optionIndex + 1].StartsWith("--"))
        {
            var user = arguments[optionIndex + 1].Trim();
            if (user.Length > 0) return user;
        }

        return WindowsIdentity.GetCurrent().Name;
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (hubConnection is not null) await hubConnection.DisposeAsync();
        httpClient.Dispose();
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (hubConnection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(MessageInput.Text)) return;
        await hubConnection.SendAsync("SendMessage", new SendMessageRequest { Content = MessageInput.Text, Kind = MessageKind.Markdown });
        MessageInput.Clear();
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Send_Click(sender, e);
            e.Handled = true;
        }
    }

    private void UploadImage_Click(object sender, RoutedEventArgs e) => UploadMedia(new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" });
    private void UploadVideo_Click(object sender, RoutedEventArgs e) => UploadMedia(new[] { ".mp4", ".webm", ".mov", ".avi" });

    private async void UploadMedia(string[] extensions)
    {
        var dialog = new OpenFileDialog { Filter = $"媒體檔案|{string.Join(";", extensions.Select(extension => "*" + extension))}" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(File.OpenRead(dialog.FileName)), "file", Path.GetFileName(dialog.FileName));
            using var response = await httpClient.PostAsync($"{ServiceUrl}/api/chat/upload", content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "上傳失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddMessage(ChatMessage message)
    {
        var card = new Border { Background = message.UserName == windowsUser ? new SolidColorBrush(Color.FromRgb(232, 241, 235)) : Brushes.WhiteSmoke, Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(4) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = $"{message.UserName}  ·  {message.SentAt.ToLocalTime():HH:mm}", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(49, 80, 68)) });
        if (message.Kind == MessageKind.Markdown)
        {
            try
            {
                content.Children.Add(RenderMarkdown(message.Content));
            }
            catch (Exception)
            {
                content.Children.Add(new TextBlock { Text = message.Content, TextWrapping = TextWrapping.Wrap });
            }
        }
        else if (message.Kind == MessageKind.Image) content.Children.Add(new Image { Source = new BitmapImage(new Uri(ServiceUrl + message.Content)), MaxWidth = 560, MaxHeight = 360, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 0) });
        else content.Children.Add(new MediaElement { Source = new Uri(ServiceUrl + message.Content), MaxWidth = 560, MaxHeight = 360, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, Margin = new Thickness(0, 8, 0, 0) });
        card.Child = content;
        MessagesPanel.Children.Add(card);
        MessagesScrollViewer.ScrollToEnd();
    }

    private static FlowDocumentScrollViewer RenderMarkdown(string markdown)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 35, 40))
        };
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        RenderBlocks(document, Markdown.Parse(markdown, pipeline));
        return new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static void RenderBlocks(FlowDocument document, ContainerBlock markdown)
    {
        foreach (var block in markdown)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    AddInlineBlock(document, heading.Inline, heading.Level, false);
                    break;
                case ParagraphBlock paragraph:
                    AddInlineBlock(document, paragraph.Inline, 0, false);
                    break;
                case MarkdownTable table:
                    AddTable(document, table);
                    break;
                case QuoteBlock quote:
                    var quoteDocument = new FlowDocument { PagePadding = new Thickness(0) };
                    RenderBlocks(quoteDocument, quote);
                    foreach (var quoteBlock in quoteDocument.Blocks)
                    {
                        quoteBlock.BorderBrush = new SolidColorBrush(Color.FromRgb(9, 105, 218));
                        quoteBlock.BorderThickness = new Thickness(4, 0, 0, 0);
                        quoteBlock.Padding = new Thickness(14, 0, 0, 0);
                        quoteBlock.Foreground = new SolidColorBrush(Color.FromRgb(101, 109, 118));
                        document.Blocks.Add(quoteBlock);
                    }
                    break;
                case FencedCodeBlock code:
                    AddCodeBlock(document, string.Join(Environment.NewLine, code.Lines.Lines));
                    break;
                case ThematicBreakBlock:
                    document.Blocks.Add(new Paragraph { BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222)), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 12, 0, 12) });
                    break;
                case ListBlock list:
                    RenderList(document, list);
                    break;
            }
        }
    }

    private static void AddTable(FlowDocument document, MarkdownTable table)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
        var rows = table.OfType<MarkdownTableRow>().ToList();
        var columnCount = Math.Max(1, table.ColumnDefinitions.Count);
        columnCount = Math.Max(columnCount, rows.SelectMany(row => row.OfType<MarkdownTableCell>()).Select(cell => Math.Max(0, cell.ColumnIndex) + Math.Max(1, cell.ColumnSpan)).DefaultIfEmpty(1).Max());
        for (var column = 0; column < columnCount; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var nextColumn = 0;
            foreach (var cell in rows[rowIndex].OfType<MarkdownTableCell>())
            {
                var cellText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Padding = new Thickness(10, 7, 10, 7), MinWidth = 70 };
                foreach (var paragraph in cell.OfType<ParagraphBlock>()) AddInlines(cellText.Inlines, paragraph.Inline);
                if (rows[rowIndex].IsHeader) cellText.FontWeight = FontWeights.SemiBold;
                var columnIndex = cell.ColumnIndex >= 0 ? cell.ColumnIndex : nextColumn;
                columnIndex = Math.Clamp(columnIndex, 0, columnCount - 1);
                var columnSpan = Math.Clamp(Math.Max(1, cell.ColumnSpan), 1, columnCount - columnIndex);
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = rows[rowIndex].IsHeader ? new SolidColorBrush(Color.FromRgb(246, 248, 250)) : Brushes.White,
                    Child = cellText
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                Grid.SetColumnSpan(border, columnSpan);
                grid.Children.Add(border);
                nextColumn = columnIndex + columnSpan;
            }
        }
        document.Blocks.Add(new BlockUIContainer(grid));
    }

    private static void RenderList(FlowDocument document, ListBlock list)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] is not ListItemBlock item) continue;
            foreach (var child in item)
            {
                if (child is ParagraphBlock paragraph)
                {
                    var prefix = list.IsOrdered ? $"{index + 1}.  " : "•  ";
                    var block = new Paragraph { Margin = new Thickness(18, 4, 0, 4) };
                    block.Inlines.Add(new Run(prefix));
                    AddInlines(block.Inlines, paragraph.Inline);
                    document.Blocks.Add(block);
                }
            }
        }
    }

    private static void AddInlineBlock(FlowDocument document, ContainerInline? inline, int headingLevel, bool isListItem)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, headingLevel == 0 ? 7 : 16, 0, headingLevel == 0 ? 7 : 8) };
        AddInlines(paragraph.Inlines, inline);
        if (headingLevel > 0)
        {
            paragraph.FontSize = headingLevel switch { 1 => 32, 2 => 24, _ => 20 };
            paragraph.FontWeight = FontWeights.SemiBold;
            paragraph.BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222));
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
        }
        document.Blocks.Add(paragraph);
    }

    private static void AddInlines(InlineCollection target, ContainerInline? inline)
    {
        if (inline is null) return;
        foreach (var child in inline)
        {
            switch (child)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromRgb(246, 248, 250)) });
                    break;
                case EmphasisInline emphasis:
                    var emphasisText = new Span();
                    AddInlines(emphasisText.Inlines, emphasis);
                    if (emphasis.DelimiterCount >= 2) emphasisText.FontWeight = FontWeights.Bold;
                    else emphasisText.FontStyle = FontStyles.Italic;
                    target.Add(emphasisText);
                    break;
                case LinkInline link:
                    var hyperlink = new Hyperlink { NavigateUri = link.Url is null ? null : new Uri(link.Url), Foreground = new SolidColorBrush(Color.FromRgb(9, 105, 218)) };
                    AddInlines(hyperlink.Inlines, link);
                    target.Add(hyperlink);
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
            }
        }
    }

    private static void AddCodeBlock(FlowDocument document, string code)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(14, 10, 14, 10), Background = new SolidColorBrush(Color.FromRgb(246, 248, 250)), FontFamily = new FontFamily("Consolas"), FontSize = 13 };
        paragraph.Inlines.Add(new Run(code));
        document.Blocks.Add(paragraph);
    }

    private void AddSystemMessage(string text) => AddMessage(new ChatMessage { UserName = "ConvoHub", Content = text });

    private void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasContent = !string.IsNullOrWhiteSpace(MessageInput.Text);
        MarkdownPreviewBorder.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        MarkdownPreview.Content = hasContent ? RenderMarkdown(MessageInput.Text) : null;
    }
}