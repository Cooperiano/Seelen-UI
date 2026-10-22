//! Transaction boundary, shared by the installed host and isolated tests.
use super::preferences_policy;
use serde_json::Value;
use std::{fs, hash::{DefaultHasher,Hash,Hasher}, io::{Read,Write}, path::Path};

const MAX_BYTES: u64 = 131072;
pub struct Document { pub data: Value, pub revision: String, bytes: Vec<u8> }
pub trait Runtime {
    fn stop(&mut self) -> Result<(),String>;
    fn resume(&mut self) -> Result<(),String>;
}

pub fn read(path:&Path) -> Result<Document,String> {
    let file=fs::File::open(path).map_err(|e|e.to_string())?;
    let mut bytes=Vec::new();
    file.take(MAX_BYTES+1).read_to_end(&mut bytes).map_err(|e|e.to_string())?;
    if bytes.len() as u64 > MAX_BYTES { return Err("Settings file is too large".into()); }
    let data:Value=serde_json::from_slice(&bytes).map_err(|e|e.to_string())?;
    if !data.is_object() { return Err("Settings document is not an object".into()); }
    let mut hash=DefaultHasher::new();bytes.hash(&mut hash);
    Ok(Document{data,revision:format!("{:016x}",hash.finish()),bytes})
}

pub fn commit(
    path:&Path,module:&str,expected:&str,patch:&Value,runtime:&mut impl Runtime,
    mut atomic_write:impl FnMut(&Path,&[u8])->Result<(),String>,
) -> Result<Document,String> {
    let before=read(path)?;
    if before.revision != expected { return Err("Settings changed elsewhere. Reload before saving.".into()); }
    let next=preferences_policy::apply(module,&before.data,patch)?;
    if next == before.data { return Ok(before); }
    let bytes=serde_json::to_vec_pretty(&next).map_err(|e|e.to_string())?;
    if bytes.len() as u64 > MAX_BYTES { return Err("Settings patch is too large".into()); }
    runtime.stop()?;
    let mut written=false;
    let result=(|| {
        if read(path)?.revision != expected { return Err("Settings changed during preparation. Reload before saving.".into()); }
        let backup=path.with_extension(format!("suite-{}.bak",before.revision));
        match fs::OpenOptions::new().write(true).create_new(true).open(&backup) {
            Ok(mut file) => { file.write_all(&before.bytes).map_err(|e|e.to_string())?;file.sync_all().map_err(|e|e.to_string())?; }
            Err(error) if error.kind()==std::io::ErrorKind::AlreadyExists => {
                if fs::read(&backup).map_err(|e|e.to_string())? != before.bytes { return Err("Settings backup differs; original preserved".into()); }
            }
            Err(error) => return Err(error.to_string()),
        }
        atomic_write(path,&bytes)?;written=true;
        let observed=read(path)?;
        if observed.data != next { return Err("Saved settings readback differs".into()); }
        runtime.resume()?;
        Ok(observed)
    })();
    if let Err(error)=result {
        let restored=if written {
            // Do not overwrite a newer edit while rolling back our own write.
            match fs::read(path) {
                Ok(current) if current==bytes => atomic_write(path,&before.bytes),
                _ => Err("Settings changed externally; rollback refused".into()),
            }
        } else {Ok(())};
        let resumed=runtime.resume();
        return Err(format!("{error}; original restoration: {restored:?}; component recovery: {resumed:?}"));
    }
    result
}
