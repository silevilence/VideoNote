const { chromium } = require('../../work-tests/browser/node_modules/playwright');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const assert = require('node:assert/strict');
(async () => {
 const mock = http.createServer(async (req,res) => {
  let body=''; for await (const chunk of req) body+=chunk;
  if (req.url.endsWith('/audio/transcriptions')) {
   res.setHeader('Content-Type','application/json');
   res.end(JSON.stringify({segments:[{start:0,end:1,text:'Synthetic audio evidence.'}]})); return;
  }
  const parsed=JSON.parse(body);
  const report=JSON.stringify(parsed).includes('依据全部分段笔记');
  const text=report?'REPORT_READY':'STREAM_BEGIN';
  res.setHeader('Content-Type','text/event-stream');
  const frame=(content,finish=null)=>'data: '+JSON.stringify({id:'test',object:'chat.completion.chunk',created:1,model:'test',
    choices:[{index:0,delta:{role:'assistant',content},finish_reason:finish}]})+'\n\n';
  res.write(frame(text));
  setTimeout(()=>{if(!res.destroyed)res.end(frame(' complete.','stop')+'data: [DONE]\n\n');},2000);
 });
 await new Promise(r=>mock.listen(0,'127.0.0.1',r));
 const dir=path.resolve('work-tests/pipeline-publish');
 const port=5196, base='http://127.0.0.1:'+port;
 const log=fs.openSync('work-tests/browser/pipeline-server.log','w');
 const server=spawn('dotnet',['VideoNote.Server.dll','--urls',base],{cwd:dir,windowsHide:true,stdio:['ignore',log,log],
   env:{...process.env,ASPNETCORE_ENVIRONMENT:'Production',Storage__RootPath:'browser-'+Date.now(),
     Ffmpeg__FramesPerSecond:'0.02',Pipeline__MaxOutputTokens:'512'}});
 let browser;
 try {
  for(let i=0;i<120;i++) {
   try {if((await fetch(base+'/api/tasks')).ok)break;}catch{}
   if(server.exitCode!==null)throw new Error('test server exited');
   await new Promise(r=>setTimeout(r,500));
  }
  const post=async(route,body)=>{const r=await fetch(base+route,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});assert.ok(r.ok,await r.clone().text());return r.json();};
  const provider=await post('/api/providers',{name:'Browser pipeline',protocol:'openai-compatible',baseUrl:'http://127.0.0.1:'+mock.address().port+'/v1',transcriptionModel:'test-whisper'});
  const model=await post('/api/models',{providerId:provider.id,modelId:'test-model',contextWindow:128000,supportsImage:true,supportsStreaming:true});
  browser=await chromium.launch({channel:'msedge',headless:true});
  const page=await browser.newPage(); const errors=[]; page.on('pageerror',e=>errors.push(e.message)); page.on('dialog',d=>d.accept());
  await page.goto(base+'/tasks/new');
  const textModel=await post('/api/models',{providerId:provider.id,modelId:'text-only',contextWindow:128000,supportsStreaming:true});
  const videoModel=await post('/api/models',{providerId:provider.id,modelId:'video-only',contextWindow:128000,supportsVideo:true,supportsStreaming:true});
  await page.reload();

  await page.locator('#task-model option[value="'+model.id+'"]').waitFor({state:'attached'});
  assert.equal(await page.locator('#task-model option[value="'+textModel.id+'"]').count(),0);
  await page.getByRole('radio').filter({hasText:'直接理解'}).click();
  await page.locator('#task-model option[value="'+videoModel.id+'"]').waitFor({state:'attached'});
  assert.equal(await page.locator('#task-model option[value="'+model.id+'"]').count(),0);
  await page.getByRole('radio').filter({hasText:'字幕理解'}).click();
  for(const id of [model.id,textModel.id,videoModel.id])await page.locator('#task-model option[value="'+id+'"]').waitFor({state:'attached'});
  await page.getByRole('radio').filter({hasText:'直接理解'}).click();
  await page.getByLabel('显示全部模型（手动覆盖）').check();
  await page.locator('#task-model').selectOption(textModel.id);
  await page.getByTestId('capability-warning').waitFor();
  await page.locator('#video-file').setInputFiles({name:'override.mkv',mimeType:'video/x-matroska',buffer:fs.readFileSync('tests/fixtures/sample.mkv')});
  const overrideRequest=page.waitForResponse(r=>r.url().includes('/api/tasks/upload')&&r.request().method()==='POST');
  await page.getByRole('button',{name:'上传并创建任务'}).click();
  const submitted=await overrideRequest;
  assert.equal(submitted.status(),201);
  const submittedTask=await submitted.json();
  await page.waitForURL(/\/tasks\/[0-9a-f-]{36}$/);
  await page.goto(base+'/tasks/new');
  await page.locator('#task-model').selectOption(model.id);
  await page.locator('#task-prompt-content').fill('请用 Markdown 汇总。INLINE_SNAPSHOT');
  await page.locator('#video-file').setInputFiles(path.resolve('tests/fixtures/sample.mkv'));
  await page.getByRole('button',{name:'上传并创建任务'}).click();
  await page.waitForURL(/\/tasks\/[0-9a-f-]{36}$/);
  await page.locator('pre').filter({hasText:'STREAM_BEGIN'}).first().waitFor({timeout:60000});
  await page.screenshot({path:'work-tests/browser/pipeline-stream.png',fullPage:true});
  await page.getByTestId('final-report').filter({hasText:'REPORT_READY complete.'}).waitFor({timeout:30000});
  await page.getByText('请用 Markdown 汇总。INLINE_SNAPSHOT',{exact:true}).waitFor();
  const taskUrl=page.url();
  await page.reload();

  await page.getByTestId('final-report').filter({hasText:'REPORT_READY complete.'}).waitFor();
  await page.locator('details').filter({hasText:'STREAM_BEGIN complete.'}).waitFor();
  await page.screenshot({path:'work-tests/browser/pipeline-complete.png',fullPage:true});
  let modelsAvailable=false, promptsAvailable=false;
  await page.route(base+'/api/models', r=>modelsAvailable ? r.continue() :
    r.fulfill({status:503,contentType:'application/json',body:'{"message":"metadata unavailable"}'}));
  await page.route(base+'/api/prompts', r=>promptsAvailable ? r.continue() :
    r.fulfill({status:503,contentType:'application/json',body:'{"message":"metadata unavailable"}'}));
  await page.reload();
  const metadataError=page.getByText('模型或模板信息暂时加载失败，正在自动重试。',{exact:true});
  await metadataError.waitFor();
  await page.locator('.kv-grid > div').filter({hasText:'分析模型'}).getByText('暂不可用',{exact:true}).waitFor();
  await page.waitForResponse(r=>r.url()===base+'/api/models'&&r.status()===503);
  assert.ok(await metadataError.isVisible());
  modelsAvailable=true;
  await page.locator('.kv-grid > div').filter({hasText:'分析模型'}).getByText('test-model',{exact:true}).waitFor();
  assert.ok(await metadataError.isVisible());
  promptsAvailable=true;
  await metadataError.waitFor({state:'hidden'});
  await page.unroute(base+'/api/models'); await page.unroute(base+'/api/prompts');
  const currentTaskApi=base+'/api/tasks/'+taskUrl.split('/').pop();
  await page.route(currentTaskApi,r=>r.request().method()==='DELETE' ?
    r.fulfill({status:409,contentType:'application/json',body:'{"message":"audit-delete-conflict"}'}) : r.continue());
  await page.getByRole('button',{name:'删除任务',exact:true}).click();
  await page.getByText('audit-delete-conflict',{exact:true}).waitFor();
  await page.waitForResponse(r=>r.url()===currentTaskApi&&r.request().method()==='GET');
  await page.waitForResponse(r=>r.url()===currentTaskApi&&r.request().method()==='GET');
  assert.ok(await page.getByText('audit-delete-conflict',{exact:true}).isVisible());
  await page.unroute(currentTaskApi);
  console.log('PASS: metadata retries recover and polling preserves action errors');
  await page.setViewportSize({width:390,height:844});
  await page.waitForFunction(()=>document.querySelector('.sidebar').getBoundingClientRect().right<=0);
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await page.screenshot({path:'work-tests/browser/pipeline-mobile.png',fullPage:true});
  console.log('PASS: baseline pipeline and responsive checks');
  // Blazor reuses the route page: a failed destination must never retain the previous task's actions.
  const destination=submittedTask.id;
  const destinationApi=base+'/api/tasks/'+destination;
  await page.route(destinationApi, route=>route.fulfill({status:503,contentType:'application/json',body:'{"message":"route load failed"}'}));
  await page.evaluate(id=>{const a=document.createElement('a');a.href='/tasks/'+id;a.id='audit-route-link';a.textContent='switch task';document.body.append(a);},destination);
  await page.locator('#audit-route-link').click();
  await page.getByText('route load failed',{exact:true}).waitFor();
  assert.equal(await page.getByRole('heading',{name:'sample.mkv',exact:true}).count(),0);
  assert.equal(await page.getByRole('button',{name:'删除任务',exact:true}).count(),0);
  await page.unroute(destinationApi);
  await page.goto(taskUrl);
  await page.getByTestId('final-report').filter({hasText:'REPORT_READY complete.'}).waitFor();
  await page.goto(base+'/tasks');
  await page.getByRole('link',{name:'sample.mkv',exact:true}).waitFor();
  const response=await fetch(base+'/api/tasks?fileName=cancel.mkv&mode=SampledFrames&modelConfigId='+model.id,
    {method:'POST',headers:{'Content-Type':'application/octet-stream'},body:fs.readFileSync('tests/fixtures/sample.mkv')});
  const canceled=await response.json(); assert.ok(response.ok);
  await page.goto(base+'/tasks/'+canceled.id);
  await page.getByRole('button',{name:'取消分析',exact:true}).click();
  await page.getByRole('button',{name:'取消分析',exact:true}).waitFor({state:'hidden'});
  assert.equal((await (await fetch(base+'/api/tasks/'+canceled.id)).json()).status,6);
  await page.goto(base+'/tasks');
  await page.locator('.item').filter({hasText:'override.mkv'}).getByRole('button',{name:'删除任务及物料',exact:true}).click();
  const deleteNotice=page.getByText('已删除任务「override.mkv」。',{exact:true});
  await deleteNotice.waitFor();
  await page.waitForResponse(r=>r.url()===base+'/api/tasks'&&r.request().method()==='GET');
  await page.waitForResponse(r=>r.url()===base+'/api/tasks'&&r.request().method()==='GET');
  assert.ok(await deleteNotice.isVisible());
  assert.deepEqual(errors,[]);
  console.log('PASS: browser upload -> live map text -> final report -> reload persistence; list updates and cancellation; desktop/mobile screenshots');
 } finally {
  if(browser)await browser.close();
  server.kill(); mock.closeAllConnections(); await new Promise(r=>mock.close(r)); fs.closeSync(log);
 }
})().catch(e=>{console.error(e);process.exitCode=1});
