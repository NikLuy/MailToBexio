using MailToBexio.Configuration;
using MailToBexio.Models;
using MailToBexio.Services;
using MailToBexio.Services.AI;
using MailToBexio.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using NSubstitute;

namespace MailToBexio.Tests;

public class MailProcessingWorkerTests
{
    private static MailProcessingWorker BuildWorker(
        IGraphMailService? graph = null,
        IAIService? ai = null,
        IBexioService? bexio = null)
    {
        return new MailProcessingWorker(
            graph ?? Substitute.For<IGraphMailService>(),
            ai ?? Substitute.For<IAIService>(),
            bexio ?? Substitute.For<IBexioService>(),
            Options.Create(new WorkerSettings { IntervalMinutes = 0 }),
            NullLogger<MailProcessingWorker>.Instance);
    }

    private static Message FakeMessage(string id = "msg-001", string body = "Mail-Inhalt") =>
        new() { Id = id, Body = new ItemBody { Content = body } };

    [Fact]
    public async Task ProcessCycle_NoMessages_NeverCallsAiOrBexio()
    {
        var graph = Substitute.For<IGraphMailService>();
        var ai = Substitute.For<IAIService>();
        var bexio = Substitute.For<IBexioService>();

        graph.GetUnreadMessagesAsync(Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<Message>>([]));

        var worker = BuildWorker(graph, ai, bexio);
        await worker.ProcessCycleAsync(CancellationToken.None);

        await ai.DidNotReceive().ExtractCustomerInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await bexio.DidNotReceive().CreateContactIfNotExistsAsync(Arg.Any<CustomerData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessCycle_ValidMessage_CreatesContactAndMarksRead()
    {
        var graph = Substitute.For<IGraphMailService>();
        var ai = Substitute.For<IAIService>();
        var bexio = Substitute.For<IBexioService>();

        var message = FakeMessage();
        graph.GetUnreadMessagesAsync(Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<Message>>([message]));

        var extracted = new CustomerData { Email = "max@muster.ch", LastName = "Muster" };
        ai.ExtractCustomerInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<CustomerData?>(extracted));

        bexio.CreateContactIfNotExistsAsync(Arg.Any<CustomerData>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(true));

        var worker = BuildWorker(graph, ai, bexio);
        await worker.ProcessCycleAsync(CancellationToken.None);

        await bexio.Received(1).CreateContactIfNotExistsAsync(extracted, Arg.Any<CancellationToken>());
        await graph.Received(1).MarkAsReadAsync("msg-001", Arg.Any<CancellationToken>());
        await graph.DidNotReceive().MoveToErrorFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessCycle_AiReturnsNull_MovesToErrorFolder()
    {
        var graph = Substitute.For<IGraphMailService>();
        var ai = Substitute.For<IAIService>();
        var bexio = Substitute.For<IBexioService>();

        graph.GetUnreadMessagesAsync(Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<Message>>([FakeMessage()]));

        ai.ExtractCustomerInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<CustomerData?>(null));

        var worker = BuildWorker(graph, ai, bexio);
        await worker.ProcessCycleAsync(CancellationToken.None);

        await graph.Received(1).MoveToErrorFolderAsync("msg-001", Arg.Any<CancellationToken>());
        await bexio.DidNotReceive().CreateContactIfNotExistsAsync(Arg.Any<CustomerData>(), Arg.Any<CancellationToken>());
        await graph.DidNotReceive().MarkAsReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessCycle_AiReturnsInvalidData_MovesToErrorFolder()
    {
        var graph = Substitute.For<IGraphMailService>();
        var ai = Substitute.For<IAIService>();
        var bexio = Substitute.For<IBexioService>();

        graph.GetUnreadMessagesAsync(Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<Message>>([FakeMessage()]));

        // Keine E-Mail-Adresse → IsValid() = false
        ai.ExtractCustomerInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<CustomerData?>(new CustomerData { LastName = "Muster" }));

        var worker = BuildWorker(graph, ai, bexio);
        await worker.ProcessCycleAsync(CancellationToken.None);

        await graph.Received(1).MoveToErrorFolderAsync("msg-001", Arg.Any<CancellationToken>());
        await bexio.DidNotReceive().CreateContactIfNotExistsAsync(Arg.Any<CustomerData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessCycle_MultipleMessages_ProcessesAll()
    {
        var graph = Substitute.For<IGraphMailService>();
        var ai = Substitute.For<IAIService>();
        var bexio = Substitute.For<IBexioService>();

        graph.GetUnreadMessagesAsync(Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<Message>>([
                 FakeMessage("msg-001"),
                 FakeMessage("msg-002"),
                 FakeMessage("msg-003")
             ]));

        ai.ExtractCustomerInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<CustomerData?>(
              new CustomerData { Email = "test@example.com", LastName = "Test" }));

        bexio.CreateContactIfNotExistsAsync(Arg.Any<CustomerData>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(true));

        var worker = BuildWorker(graph, ai, bexio);
        await worker.ProcessCycleAsync(CancellationToken.None);

        await ai.Received(3).ExtractCustomerInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await graph.Received(1).MarkAsReadAsync("msg-001", Arg.Any<CancellationToken>());
        await graph.Received(1).MarkAsReadAsync("msg-002", Arg.Any<CancellationToken>());
        await graph.Received(1).MarkAsReadAsync("msg-003", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessCycle_DuplicateContact_StillMarksRead()
    {
        var graph = Substitute.For<IGraphMailService>();
        var ai = Substitute.For<IAIService>();
        var bexio = Substitute.For<IBexioService>();

        graph.GetUnreadMessagesAsync(Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<Message>>([FakeMessage()]));

        ai.ExtractCustomerInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<CustomerData?>(
              new CustomerData { Email = "existing@muster.ch", LastName = "Muster" }));

        // Kontakt bereits vorhanden → false
        bexio.CreateContactIfNotExistsAsync(Arg.Any<CustomerData>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));

        var worker = BuildWorker(graph, ai, bexio);
        await worker.ProcessCycleAsync(CancellationToken.None);

        // Mail trotzdem als gelesen markieren
        await graph.Received(1).MarkAsReadAsync("msg-001", Arg.Any<CancellationToken>());
    }
}
