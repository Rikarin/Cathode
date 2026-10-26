using Cathode.Spikes.GcLoad;


// Spike S2 harness. Modes:
//   audio  --device <uid> --nam <file> [--buffer 64] [--warmup 5] [--seconds 20] [--server] [--port 5199]
//          [--alloc-storm <MB/s>] [--sustained-low-latency] [--label x] [--results file.jsonl]
//   server [--port 5199] [--seconds 60]
//   load   [--url http://127.0.0.1:5199] [--seconds 25] [--concurrency 8] [--rate 0 (unthrottled)]

var options = Options.Parse(args.Skip(1));
return args.FirstOrDefault() switch {
    "audio" => await AudioMode.Run(options),
    "server" => await ServerMode.Run(options),
    "load" => await LoadMode.Run(options),
    _ => Usage()
};

static int Usage() {
    Console.Error.WriteLine("usage: Cathode.Spikes.GcLoad audio|server|load [options] (see Program.cs)");
    return 2;
}
