import fs from 'node:fs';

const files = process.argv.slice(2);

for (const file of files) {
  let text = fs.readFileSync(file, 'utf8');
  if (text.charCodeAt(0) === 0xfeff) text = text.slice(1);

  const suspicious = /[ÃÂ][\u0080-\u00bf\u2018\u2019\u201c\u201d\u2013\u2014\u2026]/.test(text) || /Ã[\u00a0-\u00ff]/.test(text);
  if (!suspicious) {
    console.log(`ok       ${file}`);
    continue;
  }

  const repaired = Buffer.from(text, 'latin1').toString('utf8');
  const stillBad = /Ã[\u00a0-\u00ff]/.test(repaired) || /Â[\u00a0-\u00ff]/.test(repaired);
  console.log(`${stillBad ? 'PARTIAL ' : 'fixed   '} ${file}`);
  fs.writeFileSync(file, repaired, 'utf8');
}
