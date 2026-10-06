using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrintTool.Ui.Shared.Controls;

/// <summary>
/// Entrada de código de 6 dígitos estilo "passcode" (caixas separadas, uma por dígito,
/// avançando o foco sozinho) — usada pra digitar o código do app autenticador no pareamento.
/// </summary>
public partial class PasscodeBox : UserControl
{
    private const int DigitCount = 6;
    private static readonly Regex DigitsOnly = new("^[0-9]+$", RegexOptions.Compiled);

    private readonly TextBox[] _boxes = new TextBox[DigitCount];
    private bool _suppressCodeCallback;

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(PasscodeBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCodePropertyChanged));

    /// <summary>Dígitos digitados até agora (pode ter menos de 6 caracteres enquanto o usuário digita).</summary>
    public string Code
    {
        get => (string)GetValue(CodeProperty);
        private set => SetValue(CodeProperty, value);
    }

    /// <summary>Disparado quando os 6 dígitos foram preenchidos.</summary>
    public event EventHandler? Completed;

    public PasscodeBox()
    {
        InitializeComponent();
        BuildBoxes();
    }

    private void BuildBoxes()
    {
        for (int i = 0; i < DigitCount; i++)
        {
            var box = new TextBox
            {
                Width = 44,
                Height = 56,
                Margin = new Thickness(5, 0, 5, 0),
                FontSize = 24,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                MaxLength = DigitCount, // permite colar o código inteiro numa caixa só
                Tag = i,
            };
            box.Style = TryFindResource("TextBoxStyle") as Style;
            box.PreviewTextInput += OnPreviewTextInput;
            box.PreviewKeyDown += OnPreviewKeyDown;
            box.TextChanged += OnTextChanged;
            DataObject.AddPastingHandler(box, OnPaste);

            _boxes[i] = box;
            RootPanel.Children.Add(box);
        }
    }

    public void Clear()
    {
        _suppressCodeCallback = true;
        foreach (TextBox box in _boxes)
        {
            box.Text = string.Empty;
        }
        _suppressCodeCallback = false;

        Code = string.Empty;
        _boxes[0].Focus();
    }

    public void FocusFirst() => _boxes[0].Focus();

    /// <summary>
    /// Reage a atribuições externas de <see cref="Code"/> (ex.: a ViewModel limpando o campo
    /// via binding TwoWay depois de um pareamento) refletindo o valor nas caixas visuais —
    /// sem isso, só o valor interno mudaria, sem a tela acompanhar.
    /// </summary>
    private static void OnCodePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (PasscodeBox)d;
        string newCode = (string?)e.NewValue ?? string.Empty;

        box._suppressCodeCallback = true;
        for (int i = 0; i < DigitCount; i++)
        {
            box._boxes[i].Text = i < newCode.Length ? newCode[i].ToString() : string.Empty;
        }
        box._suppressCodeCallback = false;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            return;
        }

        string pasted = ((string)e.DataObject.GetData(DataFormats.Text)).Trim();
        if (pasted.Length == 0 || !DigitsOnly.IsMatch(pasted))
        {
            e.CancelCommand();
            return;
        }

        e.CancelCommand();
        DistributeDigits((TextBox)sender, pasted);
    }

    private void DistributeDigits(TextBox origin, string digits)
    {
        int startIndex = Array.IndexOf(_boxes, origin);
        if (startIndex < 0)
        {
            return;
        }

        _suppressCodeCallback = true;
        int digitIndex = 0;
        for (int boxIndex = startIndex; boxIndex < DigitCount && digitIndex < digits.Length; boxIndex++, digitIndex++)
        {
            _boxes[boxIndex].Text = digits[digitIndex].ToString();
        }
        _suppressCodeCallback = false;

        UpdateCode();

        int nextFocusIndex = Math.Min(startIndex + digitIndex, DigitCount - 1);
        _boxes[nextFocusIndex].Focus();
        _boxes[nextFocusIndex].CaretIndex = _boxes[nextFocusIndex].Text.Length;

        if (digitIndex >= DigitCount - startIndex)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        int index = (int)box.Tag;

        if (e.Key == Key.Back && box.Text.Length == 0 && index > 0)
        {
            _boxes[index - 1].Focus();
            _boxes[index - 1].CaretIndex = _boxes[index - 1].Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Left && index > 0)
        {
            _boxes[index - 1].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && index < DigitCount - 1)
        {
            _boxes[index + 1].Focus();
            e.Handled = true;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressCodeCallback)
        {
            return;
        }

        var box = (TextBox)sender;
        int index = (int)box.Tag;

        // Colar mais de um dígito numa caixa só é tratado em OnPaste; aqui só sobra o caso de
        // digitação normal, um caractere por vez.
        if (box.Text.Length > 1)
        {
            return;
        }

        UpdateCode();

        if (box.Text.Length == 1 && index < DigitCount - 1)
        {
            _boxes[index + 1].Focus();
        }

        if (Code.Length == DigitCount)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateCode()
    {
        var builder = new StringBuilder(DigitCount);
        foreach (TextBox box in _boxes)
        {
            builder.Append(box.Text);
        }
        Code = builder.ToString();
    }
}
