using StoneFlow.Reporting.Domain.Audit;
using StoneFlow.Shared.Application.Audit;

namespace StoneFlow.Reporting.Infrastructure.Audit;

/// <summary>
/// Zakres wpisów obiektu (SF-22): wpisy, których obiektem jest <c>root</c>, i wpisy, których obiektem
/// nadrzędnym jest <c>root</c>. Jedyna definicja zakresu dla anonimizacji i odczytu ostatniej aktywności
/// (indeksy <c>(ObjectType, ObjectId)</c> i <c>(ParentObjectType, ParentObjectId)</c>).
/// </summary>
internal static class AuditScope
{
    public static IQueryable<AuditRecord> ForRoot(this IQueryable<AuditRecord> records, AuditObjectReference root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var type = root.Type;
        var id = root.Id;
        return records.Where(record =>
            (record.ObjectType == type && record.ObjectId == id)
            || (record.ParentObjectType == type && record.ParentObjectId == id));
    }
}
