public static class Extensions
{
    public static TResult Let<T, TResult>(this T source, Func<T, TResult> selector) => selector(source);
}