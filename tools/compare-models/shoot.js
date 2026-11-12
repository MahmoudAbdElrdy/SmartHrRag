const puppeteer = require("puppeteer-core");
const fs = require("fs");
const path = require("path");

const model = process.argv[2];
if (!model) {
  console.error("Usage: node shoot.js <model-folder-name>");
  process.exit(1);
}

const askUrl = process.env.ASK_URL || "http://localhost:4200/ask";
const chrome =
  process.env.CHROME_PATH || "C:/Program Files/Google/Chrome/Application/chrome.exe";
const questionsPath = path.join(__dirname, "questions.json");
const questions = JSON.parse(fs.readFileSync(questionsPath, "utf8").replace(/^\uFEFF/, ""));

(async () => {
  const outDir = path.join(__dirname, "out", model);
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await puppeteer.launch({
    executablePath: chrome,
    headless: true,
    args: ["--lang=ar"],
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900, deviceScaleFactor: 1.5 });
  const results = [];
  for (let i = 0; i < questions.length; i++) {
    await page.goto(askUrl, { waitUntil: "networkidle0" });
    await page.waitForSelector(".service-state.is-online", { timeout: 30000 });
    await page.click("#question", { clickCount: 3 });
    await page.$eval("#question", (el) => {
      el.value = "";
      el.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await page.type("#question", questions[i].q);
    const started = Date.now();
    await page.click("button.button-primary");
    await page.waitForSelector(".answer-panel .answer-text", { timeout: 180000 });
    await page.waitForFunction(() => !document.querySelector("button.button-primary").disabled, {
      timeout: 180000,
    });
    const secs = ((Date.now() - started) / 1000).toFixed(1);
    await new Promise((resolve) => setTimeout(resolve, 500));
    const answer = await page.$eval(".answer-panel .answer-text", (el) => el.innerText);
    const n = i + 1;
    await page.screenshot({ path: path.join(outDir, `q${n}.png`), fullPage: true });
    const card = await page.$("#ask");
    await card.screenshot({ path: path.join(outDir, `q${n}-card.png`) });
    results.push({ id: questions[i].id, q: questions[i].q, secs, answer });
    console.log(`${questions[i].id} done ${secs}s`);
  }
  fs.writeFileSync(path.join(outDir, "answers.json"), JSON.stringify(results, null, 1), "utf8");
  await browser.close();
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
