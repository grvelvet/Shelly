// Описание: Свой интерпретатор: hello.js для Node.js, аргументы и код выхода
// Shelly-Icon: E943
//
// Параметры > Окружение > Свои интерпретаторы > Добавить: название «Node», расширения .js,
// программа node (или полный путь к node.exe), аргументы {script} {args}.
// Ожидается: метка «Node» в строке, вывод с кириллицей, аргументы из поля «Аргументы»,
// "exit 5" в аргументах -> код 5. Проверки безопасности у своих интерпретаторов нет.
console.log("Привет из Node.js " + process.version);
console.log("Папка запуска: " + process.cwd());
const args = process.argv.slice(2);
console.log("Аргументов: " + args.length);
args.forEach((a, i) => console.log(`  [${i + 1}] = ${a}`));
if (args[0] === "exit") process.exit(Number(args[1] || 1));
