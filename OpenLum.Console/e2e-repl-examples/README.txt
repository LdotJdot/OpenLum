OpenLum REPL automation (UTF-8)



1) Line-by-line script (multiple user turns)

   dotnet run --project OpenLum.Console -- --repl-file path\to\script.txt

   - Each non-empty line = one user message.

   - End with a line: /quit



2) Whole file = one user message (safe for multi-line prompts)

   dotnet run --project OpenLum.Console -- --repl-file path\to\prompt.txt --repl-file-entire

   - Newlines inside the file stay inside a single turn (no accidental split).

   - Process exits after one agent run (no /quit needed).



3) Run from repo root if openlum.json has "workspace": "." so paths match.



4) Efficiency regression set (see efficiency-patterns.txt)

   - entire-01 … entire-08: --repl-file-entire (06-08 = complex accuracy/efficiency)

   - lines-02-two-turn.example.txt, lines-03-three-turn-accuracy.example.txt: multi-turn line mode

   - scripts\run-e2e-complex-batch.ps1: only entire-06..08 + lines-03

   - scripts\verify-e2e-logs.ps1: scan logs for spawn + rough turn counts

   - Conversation record (.openlum JSON): config conversation.autoSave + path, or --conversation-file, or OPENLUM_CONVERSATION_FILE. Resume: openlum-console.exe --continue file.openlum or openlum-console.exe file.openlum

   - entire-09-parallel-folder-md.example.txt + fixture-md-batch/: folder Markdown analysis; expect glob then read_many (often 2 model rounds; read_many still batches all files in one tool call).

   - entire-11 … entire-13: Desktop .docx via read; D:\Desktop\fly报奖 grep search (no exec).

   - multiline-prompt.example.txt, oneline-script.example.txt: basics

   - complex-01 … complex-10: 多样化复杂单轮任务（grep/read_many/exec/list_dir/glob/todo/memory_search/submit_plan 等组合）；运行：scripts\run-e2e-repl-complex10.ps1

   - lines-04-complex-four-turn.example.txt: 四轮 + /quit，链式 glob→grep→read→词汇（与 lines-02/03 同模式）


