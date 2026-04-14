using System;
using System.IO;
using System.Linq;
using OpenLum.Console.Models;
using OpenLum.Console.Session;
using OpenLum.Console.Tools;
using Xunit;

namespace OpenLum.Tests;

public class ConversationRecordSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesMessagesAndTodos()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openlum-conv-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "t.openlum");
        try
        {
            var messages = new[]
            {
                new ChatMessage { Role = MessageRole.User, Content = "hi" },
                new ChatMessage
                {
                    Role = MessageRole.Assistant,
                    Content = "",
                    ToolCalls =
                    [
                        new ToolCall { Id = "c1", Name = "grep", Arguments = "{\"pattern\":\"x\"}" }
                    ]
                },
                new ChatMessage
                {
                    Role = MessageRole.Tool,
                    Content = "ok",
                    ToolCallId = "c1",
                    IsToolError = false
                }
            };
            var todos = new[] { new TodoItem("1", "t", "pending") };

            ConversationRecordSerializer.Save(path, dir, "test/model", messages, todos, "plan body");

            var loaded = ConversationRecordSerializer.Load(path);
            Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(loaded.Workspace!), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(3, loaded.Messages.Count);
            Assert.Equal("hi", loaded.Messages[0].Content);
            Assert.Single(loaded.Messages[1].ToolCalls!);
            Assert.Equal("grep", loaded.Messages[1].ToolCalls![0].Name);
            Assert.Equal("ok", loaded.Messages[2].Content);
            Assert.Single(loaded.Todos);
            Assert.Equal("1", loaded.Todos[0].Id);
            Assert.Equal("plan body", loaded.Plan);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
