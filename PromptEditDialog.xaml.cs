using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace NextVoiceSync;

/// <summary>
/// ユーザーがプロンプトを編集できるダイアログウィンドウ。
/// </summary>
public partial class PromptEditDialog : Window
{
    /// <summary>
    /// ユーザーが編集したプロンプトの内容。
    /// </summary>
    public string EditedPrompt { get; private set; }

    /// <summary>
    /// `PromptEditDialog` のコンストラクタ。
    /// 指定されたプロンプトをテキストボックスにセットする。
    /// </summary>
    public PromptEditDialog(string prompt)
    {
        InitializeComponent();
        PromptTextBox.Text = prompt;
    }

    /// <summary>
    /// OKボタンがクリックされたときに、編集内容を保存してダイアログを閉じる。
    /// </summary>
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        EditedPrompt = PromptTextBox.Text;
        DialogResult = true;
    }

    /// <summary>
    /// キャンセルボタンがクリックされたときに、ダイアログを閉じる。
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
