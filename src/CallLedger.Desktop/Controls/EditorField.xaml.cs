using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CallLedger.Desktop.Controls;

public partial class EditorField : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(EditorField),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FieldKeyProperty = DependencyProperty.Register(
        nameof(FieldKey),
        typeof(string),
        typeof(EditorField),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(EditorField),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsSensitiveProperty = DependencyProperty.Register(
        nameof(IsSensitive),
        typeof(bool),
        typeof(EditorField),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsMaskedProperty = DependencyProperty.Register(
        nameof(IsMasked),
        typeof(bool),
        typeof(EditorField),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsMultilineProperty = DependencyProperty.Register(
        nameof(IsMultiline),
        typeof(bool),
        typeof(EditorField),
        new PropertyMetadata(false));

    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength),
        typeof(int),
        typeof(EditorField),
        new PropertyMetadata(0));

    public static readonly DependencyProperty CopyCommandProperty = DependencyProperty.Register(
        nameof(CopyCommand),
        typeof(ICommand),
        typeof(EditorField));

    public static readonly DependencyProperty PasteCommandProperty = DependencyProperty.Register(
        nameof(PasteCommand),
        typeof(ICommand),
        typeof(EditorField));

    public static readonly DependencyProperty ToggleSensitiveCommandProperty = DependencyProperty.Register(
        nameof(ToggleSensitiveCommand),
        typeof(ICommand),
        typeof(EditorField));

    public EditorField()
    {
        InitializeComponent();
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string FieldKey { get => (string)GetValue(FieldKeyProperty); set => SetValue(FieldKeyProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsSensitive { get => (bool)GetValue(IsSensitiveProperty); set => SetValue(IsSensitiveProperty, value); }
    public bool IsMasked { get => (bool)GetValue(IsMaskedProperty); set => SetValue(IsMaskedProperty, value); }
    public bool IsMultiline { get => (bool)GetValue(IsMultilineProperty); set => SetValue(IsMultilineProperty, value); }
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    public ICommand? CopyCommand { get => (ICommand?)GetValue(CopyCommandProperty); set => SetValue(CopyCommandProperty, value); }
    public ICommand? PasteCommand { get => (ICommand?)GetValue(PasteCommandProperty); set => SetValue(PasteCommandProperty, value); }
    public ICommand? ToggleSensitiveCommand { get => (ICommand?)GetValue(ToggleSensitiveCommandProperty); set => SetValue(ToggleSensitiveCommandProperty, value); }
}
