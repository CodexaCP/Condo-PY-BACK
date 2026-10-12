namespace Condo.Application.Models;

/// <summary>Sobre comun de los listados paginados en el servidor (unidades, usuarios, propietarios, residentes).</summary>
public class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public static class PagedQuery
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public static int NormalizePage(int page) => page < 1 ? 1 : page;
    public static int NormalizePageSize(int pageSize) => pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    /// <summary>Texto de busqueda sin espacios sobrantes; vacio si no hay.</summary>
    public static string NormalizeSearch(string? search) => (search ?? string.Empty).Trim();
}
