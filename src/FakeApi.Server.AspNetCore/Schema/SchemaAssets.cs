using System;
using System.IO;

namespace FakeApi.Server.AspNetCore.Schema
{
    public static class SchemaAssets
    {
        public static byte[] SchemaJson { get; }

        public static byte[] OpenApiYaml { get; }

        static SchemaAssets()
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "assets");
            SchemaJson = File.ReadAllBytes(Path.Combine(directory, "fake-api-schema.json"));
            OpenApiYaml = File.ReadAllBytes(Path.Combine(directory, "fake-api.openapi.yaml"));
        }
    }
}
