using System.Linq.Expressions;
using System.Reflection;

namespace Respsody.Library;

public static class ListExtensions
{
    public static void ClearWithoutZeroing<T>(this List<T> list)
        where T : struct
        => Clear<T>.Execute(list);

    private static class Clear<T>
    {
        public static readonly Action<List<T>> Execute = CreateAction();

        private static Action<List<T>> CreateAction()
        {
            var listType = typeof(List<T>);
            var sizeField = listType.GetField("_size", BindingFlags.NonPublic | BindingFlags.Instance);
            if (sizeField == null)
                return static list => list.Clear();

            var listParam = Expression.Parameter(listType, "list");
            var zero = Expression.Constant(0);
            var fieldExpression = Expression.Field(listParam, sizeField);
            var assignmentExpression = Expression.Assign(fieldExpression, zero);
            return Expression.Lambda<Action<List<T>>>(assignmentExpression, listParam).Compile();
        }
    }
}