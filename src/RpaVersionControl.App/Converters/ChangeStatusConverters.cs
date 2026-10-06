using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Converters;

public sealed class ChangeStatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is ChangeStatus status ? Describe(status) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    public static string Describe(ChangeStatus status) => status switch
    {
        ChangeStatus.Draft => "Rascunho",
        ChangeStatus.Submitted => "Aguardando QA",
        ChangeStatus.InReview => "Em revisão",
        ChangeStatus.AdjustmentsRequested => "Ajustes solicitados",
        ChangeStatus.RetrofitRequired => "Possível retrofit",
        ChangeStatus.NeedsRebase => "Reenvio necessário",
        ChangeStatus.ApprovedPendingPublish => "Aprovado — publicando",
        ChangeStatus.PublishFailed => "Falha ao publicar",
        ChangeStatus.Published => "Publicado",
        ChangeStatus.Rejected => "Rejeitado",
        ChangeStatus.Cancelled => "Cancelado",
        _ => status.ToString()
    };
}

public sealed class ChangeStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is ChangeStatus status ? BrushKeyFor(status) : "TextFillColorSecondaryBrush";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static string BrushKeyFor(ChangeStatus status) => status switch
    {
        ChangeStatus.Published => "SuccessBrush",
        ChangeStatus.Submitted or ChangeStatus.InReview => "InfoBrush",
        ChangeStatus.AdjustmentsRequested or ChangeStatus.RetrofitRequired or ChangeStatus.NeedsRebase
            or ChangeStatus.ApprovedPendingPublish => "WarningBrush",
        ChangeStatus.PublishFailed or ChangeStatus.Rejected => "DangerBrush",
        _ => "TextFillColorSecondaryBrush"
    };
}
