#!/usr/bin/node
// Local synthetic browser replay only. Copy beside the desired trace file;
// the production host and mapper still execute unchanged in the child process.
const {spawn} = require('node:child_process');
const {appendFileSync, existsSync, readFileSync} = require('node:fs');
const {dirname, join} = require('node:path');
const trace = join(dirname(__filename), 'native-frames.jsonl');
const args = process.argv.slice(2);
const config = join(dirname(__filename), 'trace-config.json');
if (existsSync(config)) args[args.length - 1] = JSON.parse(readFileSync(config, 'utf8')).modulePath;
const child = spawn(process.execPath, args, {stdio: ['pipe', 'pipe', 'pipe']});
function observe(stream, direction) {
  let buffered = '';
  stream.on('data', chunk => {
    buffered += chunk.toString('utf8');
    for (;;) {
      const end = buffered.indexOf('\n');
      if (end < 0) break;
      const frame = JSON.parse(buffered.slice(0, end));
      appendFileSync(trace, JSON.stringify({pid: process.pid, direction, frame}) + '\n');
      buffered = buffered.slice(end + 1);
    }
  });
}
observe(process.stdin, 'request');
observe(child.stdout, 'response');
process.stdin.pipe(child.stdin); child.stdout.pipe(process.stdout); child.stderr.pipe(process.stderr);
child.once('exit', code => process.exit(code ?? 1));
process.on('SIGTERM', () => child.kill('SIGTERM'));
