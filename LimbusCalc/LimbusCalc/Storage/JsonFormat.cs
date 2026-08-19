using System.Text.Json;

namespace LimbusCalc.Storage;

/// <summary>Как записывать JSON, который пользователь открывает и правит руками.</summary>
public static class JsonFormat
{
    /// <summary>
    /// С отступами и переносами строк. Файл раздувается втрое, но его читают глазами,
    /// а не только программой. Внутренние файлы в профиле пишутся без отступов:
    /// их переписывают на каждой правке и никто не открывает.
    /// </summary>
    public static readonly JsonSerializerOptions Readable = new() { WriteIndented = true };
}
