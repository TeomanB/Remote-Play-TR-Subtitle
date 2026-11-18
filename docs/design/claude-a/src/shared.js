const AC=(()=>{
const $$=(s,r=document)=>[...r.querySelectorAll(s)];
const root=document.documentElement;
const clamp=(v,a,b)=>Math.min(b,Math.max(a,v));
const DEF={orig:{x:.2,y:.79,w:.6,h:.095},trans:{x:.18,y:.635,w:.64,h:.105}};
const S={boxes:JSON.parse(JSON.stringify(DEF)),sel:'trans',running:false,locked:{orig:false,trans:false}};
const NAMES={orig:'Orijinal altyazı',trans:'Çeviri'};
const RES={x:1920,y:1080,w:1920,h:1080};
const ico=n=>`<svg class="ico sm"><use href="#i-${n}"/></svg>`;

/* ---- kutular ---- */
function render(){
  for(const n in S.boxes){const b=S.boxes[n];
    $$(`[data-box="${n}"]`).forEach(el=>{
      el.style.left=b.x*100+'%';el.style.top=b.y*100+'%';el.style.width=b.w*100+'%';el.style.height=b.h*100+'%';
      el.classList.toggle('selected',n===S.sel);el.classList.toggle('locked',S.locked[n]);});}
  const b=S.boxes[S.sel],lk=S.locked[S.sel];
  $$('[data-geo]').forEach(i=>{i.disabled=lk;if(document.activeElement!==i)i.value=Math.round(b[i.dataset.geo]*RES[i.dataset.geo]);});
  $$('[data-act="reset-box"]').forEach(x=>x.disabled=lk);
  $$('[data-lock-sel]').forEach(c=>c.checked=lk);
  $$('[data-lock]').forEach(x=>{const on=S.locked[x.dataset.lock];x.classList.toggle('on',on);x.setAttribute('aria-pressed',on);x.title=on?'Kilidi aç':'Kilitle';});
  $$('[data-select]').forEach(e=>e.classList.toggle('active',e.dataset.select===S.sel));
  $$('[data-sel-name]').forEach(e=>e.textContent=NAMES[S.sel]);
}
function select(n){S.sel=n;render();}
function setLock(n,on){S.locked[n]=on;render();}
$$('[data-box]').forEach(el=>{
  ['nw','n','ne','e','se','s','sw','w'].forEach(h=>{const i=document.createElement('i');i.className='h h-'+h;i.dataset.h=h;el.appendChild(i);});
  el.addEventListener('pointerdown',e=>{
    if(e.button!==0)return;
    const n=el.dataset.box,r=el.closest('.stage').getBoundingClientRect(),h=e.target.dataset.h||'';
    const b0={...S.boxes[n]},sx=e.clientX,sy=e.clientY,MW=.08,MH=.05;
    select(n);e.preventDefault();
    if(S.locked[n])return;
    try{el.setPointerCapture(e.pointerId);}catch(_){}
    let live=false;
    const mv=ev=>{
      if(!live){if(Math.abs(ev.clientX-sx)<4&&Math.abs(ev.clientY-sy)<4)return;live=true;}
      const dx=(ev.clientX-sx)/r.width,dy=(ev.clientY-sy)/r.height;let {x,y,w,h:hh}=b0;
      if(!h){x=clamp(x+dx,0,1-w);y=clamp(y+dy,0,1-hh);}
      else{
        if(h.includes('e'))w=clamp(b0.w+dx,MW,1-b0.x);
        if(h.includes('s'))hh=clamp(b0.h+dy,MH,1-b0.y);
        if(h.includes('w')){x=clamp(b0.x+dx,0,b0.x+b0.w-MW);w=b0.w+b0.x-x;}
        if(h.includes('n')){y=clamp(b0.y+dy,0,b0.y+b0.h-MH);hh=b0.h+b0.y-y;}
      }
      S.boxes[n]={x,y,w,h:hh};render();};
    const up=()=>{el.removeEventListener('pointermove',mv);el.removeEventListener('pointerup',up);el.removeEventListener('pointercancel',up);};
    el.addEventListener('pointermove',mv);el.addEventListener('pointerup',up);el.addEventListener('pointercancel',up);
  });
});
const ro=new ResizeObserver(es=>es.forEach(e=>{if(e.contentRect.height)e.target.style.setProperty('--k',e.contentRect.height/1080);}));
$$('.stage').forEach(s=>ro.observe(s));

/* ---- görünüm değişkenleri ---- */
function setVar(name,val){
  root.style.setProperty(name,val);
  $$(`[data-var="${name}"]`).forEach(i=>{if(i.value!=val)i.value=val;});
  $$(`[data-out="${name}"]`).forEach(o=>o.textContent=o.dataset.fmt==='pct'?Math.round(val*100)+'%':val+(o.dataset.unit||''));
  $$(`[data-swatch="${name}"]`).forEach(s=>s.classList.toggle('active',s.dataset.value.toLowerCase()===String(val).toLowerCase()));
}
Object.entries({'--sub-font':"'Segoe UI Variable Display','Segoe UI',sans-serif",'--sub-size':44,'--sub-color':'#FFFFFF','--sub-outline':'#000000','--sub-ow':3,'--sub-bg':.45}).forEach(([k,v])=>setVar(k,v));

/* ---- sekmeler ---- */
function showTab(g,n){
  $$(`[data-tabs="${g}"] [data-tab]`).forEach(b=>b.classList.toggle('active',b.dataset.tab===n));
  $$(`[data-panel^="${g}:"]`).forEach(p=>p.hidden=p.dataset.panel!==g+':'+n);
  document.dispatchEvent(new CustomEvent('tabchange',{detail:{g,n}}));
}

/* ---- çalıştır / durdur ---- */
const LINES=[
  ['We need to get out of here before the cops show up.','Polisler gelmeden buradan çıkmamız lazım.'],
  ['Meet me at the pier. Come alone.','İskelede buluşalım. Yalnız gel.'],
  ["I told you, Marco doesn't work with strangers.",'Sana söyledim, Marco yabancılarla çalışmaz.'],
  ['Nice car. Shame if something happened to it.','Güzel araba. Başına bir şey gelse yazık olur.']];
let li=0,timer;
const txt=(sel,v)=>$$(sel).forEach(e=>e.textContent=v);
function setLine(i){li=i;txt('[data-text="orig"]',LINES[i][0]);txt('[data-text="trans"]',LINES[i][1]);txt('[data-last]',LINES[i][1]);}
const HIST=[];
function tick(){setLine((li+1)%LINES.length);const ms=170+Math.round(Math.random()*120);txt('[data-ms]',ms+' ms');
  HIST.unshift([LINES[li][1],LINES[li][0]+' · '+ms+' ms']);HIST.length=Math.min(HIST.length,5);
  $$('[data-history]').forEach(el=>{el.textContent='';HIST.forEach(h=>{const d=document.createElement('div'),b=document.createElement('b'),s=document.createElement('small');b.textContent=h[0];s.textContent=h[1];d.append(b,s);el.appendChild(d);});});}
function setRun(on){
  S.running=on;document.body.classList.toggle('running',on);
  txt('[data-run-label]',on?'Durdur':'Başlat');txt('[data-status]',on?'Çalışıyor':'Hazır');
  clearInterval(timer);
  if(on){tick();timer=setInterval(tick,3000);}else txt('[data-ms]','—');
}
setLine(0);

/* ---- sözlük ---- */
const PACKS=[['GTA 6','G6','#E8456B',412,1],['Red Dead Redemption 2','RD','#B4532A',268,0],['The Last of Us Part II','TL','#3F6B4E',154,0],['God of War Ragnarök','GW','#3B6FB5',197,0],['Cyberpunk 2077','CP','#C9A400',331,0],['Genel oyun terimleri','GN','#0A8480',520,1]]
  .map(([name,ab,c,n,on])=>({name,ab,c,n,on:!!on}));
const TERMS=[['Wanted Level','Aranma Seviyesi'],['Heist','Soygun'],['Safehouse','Sığınak']];
let q='';
function renderDict(){
  const m=s=>s.toLocaleLowerCase('tr').includes(q);
  const ps=PACKS.filter(p=>m(p.name)),ts=TERMS.filter(t=>m(t[0])||m(t[1]));
  $$('[data-packs]').forEach(el=>el.innerHTML=ps.map(p=>`<div class="li"><span class="tile" style="--c:${p.c}">${p.ab}</span><span class="li-t"><b>${p.name}</b><small>${p.n} terim</small></span><label class="switch"><input type="checkbox" data-pack="${PACKS.indexOf(p)}" ${p.on?'checked':''} aria-label="${p.name}"><i></i></label></div>`).join('')||'<div class="empty">Eşleşen paket yok.</div>');
  $$('[data-terms]').forEach(el=>{el.textContent='';
    if(!ts.length){el.innerHTML='<div class="empty">Eşleşen terim yok.</div>';return;}
    ts.forEach(t=>{const d=document.createElement('div');d.className='term';d.innerHTML=`<span></span>${ico('arrow')}<span></span>`;d.children[0].textContent=t[0];d.children[2].textContent=t[1];el.appendChild(d);});});
  txt('[data-pack-count]',PACKS.filter(p=>p.on).length+' paket açık');
}
renderDict();

/* ---- olaylar ---- */
document.addEventListener('input',e=>{
  const t=e.target;
  if(t.dataset.var)setVar(t.dataset.var,t.value);
  if(t.dataset.geo){const k=t.dataset.geo,b=S.boxes[S.sel],v=(parseFloat(t.value)||0)/RES[k];
    if(k==='x')b.x=clamp(v,0,1-b.w);if(k==='y')b.y=clamp(v,0,1-b.h);
    if(k==='w')b.w=clamp(v,.08,1-b.x);if(k==='h')b.h=clamp(v,.05,1-b.y);render();}
  if(t.matches('[data-lock-sel]'))setLock(S.sel,t.checked);
  if(t.matches('[data-pack-search]')){q=t.value.trim().toLocaleLowerCase('tr');renderDict();}
  if(t.dataset.pack){PACKS[+t.dataset.pack].on=t.checked;txt('[data-pack-count]',PACKS.filter(p=>p.on).length+' paket açık');}
});
document.addEventListener('focusout',e=>{if(e.target.dataset&&e.target.dataset.geo)render();});
document.addEventListener('submit',e=>{
  if(!e.target.matches('[data-term-form]'))return;e.preventDefault();
  const [a,b]=$$('input',e.target);if(!a.value.trim()||!b.value.trim())return;
  TERMS.unshift([a.value.trim(),b.value.trim()]);a.value=b.value='';renderDict();a.focus();
});
document.addEventListener('click',e=>{
  const t=e.target.closest('[data-tab],[data-swatch],[data-select],[data-run-toggle],[data-act],[data-lock]');if(!t)return;
  if(t.dataset.lock)setLock(t.dataset.lock,!S.locked[t.dataset.lock]);
  if(t.dataset.tab)showTab(t.closest('[data-tabs]').dataset.tabs,t.dataset.tab);
  if(t.dataset.swatch)setVar(t.dataset.swatch,t.dataset.value);
  if(t.dataset.select)select(t.dataset.select);
  if(t.hasAttribute('data-run-toggle'))setRun(!S.running);
  const a=t.dataset.act;
  if(a==='reset-box'&&!S.locked[S.sel]){S.boxes[S.sel]={...DEF[S.sel]};render();}
  if(a==='check-update'){t.disabled=true;t.textContent='Denetleniyor…';setTimeout(()=>{t.disabled=false;t.textContent='Denetle';txt('[data-upd]','Sürüm 1.4.2 · güncel, az önce denetlendi');},1300);}
  if(a==='install-ocr'){t.disabled=true;t.textContent='Kuruluyor…';const bar=t.closest('.set').querySelector('.bar');bar.hidden=false;
    requestAnimationFrame(()=>requestAnimationFrame(()=>bar.firstElementChild.style.width='100%'));
    setTimeout(()=>{bar.hidden=true;t.hidden=true;$$('[data-ocr]').forEach(b=>{b.className='badge ok';b.textContent='Kurulu';});},2000);}
});
render();
return {S,DEF,render,select,setLock,showTab,setRun,setVar,$$};
})();
