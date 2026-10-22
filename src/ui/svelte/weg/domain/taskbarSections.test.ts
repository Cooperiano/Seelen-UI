import { orderTaskbarSections, temporarySectionStart } from "./taskbarSections.ts";
declare const Deno: { test(name: string, test: () => void): void };
function assertEquals(actual: unknown, expected: unknown) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) throw new Error(`${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
}
const app=(id:string,pinned:boolean)=>({id,type:"AppOrFile",pinned});
Deno.test("fixed apps and running-only apps have separate stable-order sections",()=>{
  const input=[app("run1",false),app("pin2",true),app("run2",false),app("pin1",true)];
  assertEquals(orderTaskbarSections(input).map(x=>x.id),["pin2","pin1","run1","run2"]);
  assertEquals(input.map(x=>x.id),["run1","pin2","run2","pin1"]);
  assertEquals(temporarySectionStart(orderTaskbarSections(input)),"run1");
});
Deno.test("pin/unpin moves only membership; IDs, other items and section order survive",()=>{
  const input=[{id:"start",type:"Plugin"},app("a",true),app("b",true),app("c",false),{id:"trash",type:"Plugin"}];
  const unpin=input.map(x=>x.id==="a"?{...x,pinned:false}:x);
  assertEquals(orderTaskbarSections(unpin).map(x=>x.id),["start","b","a","c","trash"]);
  const repin=orderTaskbarSections(unpin).map(x=>x.id==="c"?{...x,pinned:true}:x);
  assertEquals(orderTaskbarSections(repin).map(x=>x.id),["start","b","c","a","trash"]);
});
Deno.test("no redundant separator for empty, only fixed or only running sections",()=>{
  for(const items of [[],[app("a",true)],[app("a",false)]])assertEquals(temporarySectionStart(items),null);
});
