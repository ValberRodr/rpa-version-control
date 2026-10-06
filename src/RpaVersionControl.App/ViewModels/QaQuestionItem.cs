using CommunityToolkit.Mvvm.ComponentModel;

namespace RpaVersionControl.App.ViewModels;

public partial class QaQuestionItem : ObservableObject
{
    public int Id { get; }
    public string Question { get; }

    [ObservableProperty]
    private int _answerIndex = -1;

    [ObservableProperty]
    private string _comment = string.Empty;

    public bool IsAnswered => AnswerIndex is 0 or 1;
    public bool IsCompliant => AnswerIndex == 0;

    public QaQuestionItem(int id, string question)
    {
        Id = id;
        Question = question;
    }
}
