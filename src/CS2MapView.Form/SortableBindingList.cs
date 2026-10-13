using System.ComponentModel;

namespace CS2MapView.Form;

/// <summary>List-only sorting does not reorder service geometry or exported source collections.</summary>
internal sealed class SortableBindingList<T>(IList<T> items) : BindingList<T>(items)
{
    private PropertyDescriptor? _sort;
    private ListSortDirection _direction;
    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _sort != null;
    protected override PropertyDescriptor? SortPropertyCore => _sort;
    protected override ListSortDirection SortDirectionCore => _direction;
    protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
    {
        var comparer = Comparer<object?>.Create((a, b) => a is IComparable comparable
            ? comparable.CompareTo(b) : StringComparer.CurrentCulture.Compare(a?.ToString(), b?.ToString()));
        var values = (direction == ListSortDirection.Descending
            ? Items.OrderByDescending(x => property.GetValue(x), comparer)
            : Items.OrderBy(x => property.GetValue(x), comparer)).ToArray();
        RaiseListChangedEvents = false;
        for (var index = 0; index < values.Length; index++) Items[index] = values[index];
        RaiseListChangedEvents = true; _sort = property; _direction = direction;
        ResetBindings();
    }
    protected override void RemoveSortCore() => _sort = null;
}
