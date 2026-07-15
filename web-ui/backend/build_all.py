import subprocess
import os

backend = r'D:\web-ui\backend'
subs = ['change_gripper','load_machine','machine_status','prepare_material',
        'rack_operate','scan','start_process','transfer','unload_machine','unload_material']

results = []
for sub in subs:
    proj_dir = os.path.join(backend, sub)
    try:
        result = subprocess.run(
            ['dotnet', 'build', '-c', 'Debug'],
            cwd=proj_dir,
            capture_output=True,
            text=True,
            timeout=60
        )
        success = result.returncode == 0
        # Count errors
        error_count = result.stdout.count('error CS')
        results.append((sub, success, error_count))
        status = 'OK' if success else f'FAIL ({error_count} errors)'
        print(f'  {sub}: {status}')
    except Exception as e:
        results.append((sub, False, str(e)))
        print(f'  {sub}: EXCEPTION: {e}')

print('\n=== Summary ===')
ok = sum(1 for _, s, _ in results if s)
fail = sum(1 for _, s, _ in results if not s)
print(f'  Success: {ok}, Failed: {fail}')
for sub, success, detail in results:
    if not success:
        print(f'  FAILED: {sub} - {detail}')
