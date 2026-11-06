import fs from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
const root=path.resolve(import.meta.dirname,'..');
const deps=path.join(process.env.USERPROFILE,'.cache/codex-runtimes/codex-primary-runtime/dependencies/node');
const require=createRequire(path.join(deps,'package.json'));
const {FileBlob,SpreadsheetFile}=await import(pathToFileURL(require.resolve('@oai/artifact-tool')).href);
const template=(await fs.readdir(path.join(root,'samples'))).find(x=>x.endsWith('.xlsx'));
for(const [name,file] of [['template',path.join(root,'samples',template)],['output',path.join(root,'outputs','样例-自动生成盲点表-待审核.xlsx')]]){
 const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(file));
 const sheet=wb.worksheets.getItemAt(0);
 for(const [part,range] of [['header','A1:F10'],['footer','A105:F121']]){
  const image=await wb.render({sheetName:sheet.name,range,scale:1,format:'png'});
  await fs.writeFile(path.join(root,'outputs',`${name}-${part}.png`),new Uint8Array(await image.arrayBuffer()));
 }
 console.log(`${name}: rendered`);
}
