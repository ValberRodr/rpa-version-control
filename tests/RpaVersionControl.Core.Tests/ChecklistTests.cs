using Xunit;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Tests;

public sealed class ChecklistTests
{
    [Fact]
    public void ChecklistHasSixQuestionsAndBlocksAnyNo()
    {
        Assert.Equal(6, QaChecklistQuestions.All.Count);

        var checklist = new QaChecklist
        {
            Answers = QaChecklistQuestions.All.Select(q => new QaChecklistAnswer
            {
                QuestionId = q.Id,
                Question = q.Text,
                IsCompliant = true
            }).ToList()
        };

        Assert.True(checklist.AllCompliant);
        checklist.Answers[5].IsCompliant = false;
        Assert.False(checklist.AllCompliant);
    }

    [Fact]
    public void SixthQuestionIsRetrofitGate()
    {
        var q6 = QaChecklistQuestions.All.Single(x => x.Id == 6);
        Assert.Contains("retrofit", q6.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sustentação", q6.Text, StringComparison.OrdinalIgnoreCase);
    }
}
