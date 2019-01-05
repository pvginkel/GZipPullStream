GZipPullStream
==============

This project contains a `GZipStream` that can be pulled form.

The standard `GZipStream` pushes compressed data to another stream:

```cs
public void Compress()
{
    using (var file = File.Create("compressed.gz"))
    using (var stream = new GZipStream(file, CompressionMode.Compress))
    using (var writer = new StreamWriter(stream))
    {
        writer.Write("Some data to compress");
    }
}
```

This is fine for doing things like writing compressed data to a file.

However, when you want to return a stream that's optionally compressed,
for the caller to read from, this becomes an issue. The usual work around
is to do something like this:

```cs
public Stream GetCompressedStream()
{
    var target = new MemoryStream();

    using (var stream = new GZipStream(target, CompressionMode.Compress, leaveOpen: true))
    using (var writer = new StreamWriter(stream))
    {
        writer.Write("Some data to compress");
    }

    target.Position = 0;

    return target;
}
```

The disadvantage of this approach is that the whole compressed file needs
to be kept in memory.

This project provides a `GZipStream` that works in reverse compared to
the standard `GZipStream`. This allows for scenarios like this:

```cs
public Stream OpenFile(string fileName, bool compress)
{
    var stream = File.OpenRead(fileName);

    if (compress)
        stream = new GZipPullStream(stream);

    return stream;
}
```

This will return a stream to a caller that (of the flag is set), returns
compressed data.

The primary use case for implementing this stream was to support ASP.NET MVC
and ASP.NET Core MVC. With ASP.NET MVC, you kind of have a solution with
`PushStreamContent`:

```cs
[HttpGet]
public async Task<HttpResponseMessage> Download()
{
    var stream = File.OpenRead("file.txt");

    if (Request.Headers.AcceptEncoding.Any(p => String.Equals(p .Value, "gzip", StringComparison.OrdinalIgnoreCase)))
    {
        response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new PushStreamContent(async (target, content, context) =>
            {
                using (stream)
                using (var compressed = new GZipStream(target, CompressionMode.Compress))
                {
                    await stream.CopyToAsync(compressed);
                }
            })
        };

        response.Content.Headers.ContentEncoding.Add("gzip");

        return response;
    }

    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(stream)
    };
}
```

With the `GZipPullStream`, this becomes a lot simpler:

```cs
[HttpGet]
public async Task<HttpResponseMessage> Download()
{
    var stream = File.OpenRead("file.txt");

    bool compress = Request.Headers.AcceptEncoding.Any(p => String.Equals(p.Value, "gzip", StringComparison.OrdinalIgnoreCase));

    if (compress)
        stream = new GZipPullStream(stream);

    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(stream)
    };

    if (compress)
        response.Content.Headers.ContentEncoding.Add("gzip");

    return response;
}
```

Usage
=====

Since the complete implementation is isolated to a single class,
you can just add a NuGet reference to SharpZipLib and copy the
code. Alternatively, there is a NuGet package available at
[GZipPullStream](https://www.nuget.org/packages/GZipPullStream).

License
=======

Apache License 2.0.
