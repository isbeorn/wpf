import ast
import copy
import json
import pathlib

repository = next(parent for parent in pathlib.Path(__file__).resolve().parents
                  if (parent / 'eng/portability/Run-NinaRichText.sh').is_file())
runner = repository / 'eng/portability/Run-NinaRichText.sh'
text = runner.read_text()
start = text.index('import json,pathlib,sys\nrun=')
end = text.index('\nPY\n', start)
module = ast.parse(text[start:end])
base_classifier = next(node for node in module.body if isinstance(node, ast.Assign)
                       and any(isinstance(target, ast.Name) and target.id == 'complete' for target in node.targets))
markdown = next(node for node in module.body if isinstance(node, ast.If)
                and any(isinstance(value, ast.Constant) and value.value == 'markdown33' for value in ast.walk(node.test)))
classifier = compile(ast.Module(body=[base_classifier, *markdown.body], type_ignores=[]), str(runner), 'exec')
expected = ast.literal_eval(markdown.body[0].value)
probe = next(node for node in markdown.body if isinstance(node, ast.FunctionDef))
identities = sorted(ast.literal_eval(probe.body[0].value))

def valid(raw, code, cleanup, stderr_bytes):
    namespace = dict(raw=raw, code=code, cleanup=cleanup, stderr=stderr_bytes, unchanged=True,
                     expected=33, passed=sum(check['status'] == 'pass' for check in raw['checks']))
    exec(classifier, namespace)
    return namespace['complete']

observed = [dict(exceptionType='System.IO.FileNotFoundException', fileName=identity, hasInnerException=False,
                 namespaceProbe=True, callerStack='System.Windows.Baml2006.Baml2006SchemaContext.EnsureXmlnsAssembliesLoaded')
            for identity in identities]
baseline = dict(probe='original-NINA-richtext-control', mode='richtext-control', failed=False,
                expectedCheckCount=33, unreachedOrFailedChecks=[], checks=[dict(name=name, status='pass') for name in expected],
                dependencyFailures=observed)
raw = copy.deepcopy(baseline)
raw['dependencyFailures'] = [dict(phase='resource-loading', exception='Legacy record without per-event caller evidence')]
cases=[]
def check(name,candidate,expected,code=0,cleanup=0,stderr=0):
    actual=valid(candidate,code,cleanup,stderr)
    assert actual is expected,(name,actual,expected)
    cases.append(dict(name=name,expected=expected,actual=actual))
check('four-proven-identity-records-with-all33',baseline,True)
check('reject-legacy-event-without-caller-evidence',raw,False)
empty=copy.deepcopy(baseline);empty['dependencyFailures']=[]
check('no-dependency-events-with-all33',empty,True)
for field,value in [('fileName','Portability.Markup.AbsentTransitiveDependency, Version=1.0.0.0'),('fileName','System.Windows.Controls.Ribbon, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'),('fileName','System.Windows.Controls.Ribbon, Culture=neutral, PublicKeyToken=0000000000000000'),('exceptionType','System.BadImageFormatException'),('exceptionType','System.DllNotFoundException'),('exceptionType','System.Runtime.InteropServices.MarshalDirectiveException'),('namespaceProbe',False),('namespaceProbe',1),('hasInnerException',True),('hasInnerException',None),('callerStack','')]:
    candidate=copy.deepcopy(baseline);candidate['dependencyFailures'][0][field]=value
    check('reject-event-'+field+'-'+str(value),candidate,False)
for field in ['fileName','exceptionType','namespaceProbe','hasInnerException','callerStack']:
    candidate=copy.deepcopy(baseline);del candidate['dependencyFailures'][0][field]
    check('reject-missing-event-'+field,candidate,False)
candidate=copy.deepcopy(baseline);candidate['dependencyFailures'].append(dict(phase='required-type-ID',exceptionType='System.IO.FileNotFoundException',fileName=observed[0]['fileName'],hasInnerException=False,namespaceProbe=False,callerStack='ResolveBamlTypeToType -> ResolveAssembly'))
check('reject-one-direct-required-among-proven-optionals',candidate,False)
for field,value in [('probe','wrong'),('mode','wrong'),('expectedCheckCount',32),('failed',True),('unreachedOrFailedChecks',['missing']),('dependencyFailures',None)]:
    candidate=copy.deepcopy(baseline);candidate[field]=value
    check('reject-result-'+field,candidate,False)
for name,code,cleanup,stderr in [('child-exit',1,0,0),('cleanup',0,1,0),('stderr',0,0,1)]:
    check('reject-'+name,baseline,False,code,cleanup,stderr)
for name,mutate in [('missing-check',lambda checks:checks.pop()),('duplicate-check',lambda checks:checks.__setitem__(1,checks[0])),('reordered-checks',lambda checks:checks.reverse()),('failed-check',lambda checks:checks[0].__setitem__('status','fail'))]:
    candidate=copy.deepcopy(baseline);mutate(candidate['checks']);check('reject-'+name,candidate,False)
print(json.dumps(dict(source='Actual markdown33 runner classifier AST; saved-data controls, not a C# runtime check',
                      count=len(cases), allMatched=True, cases=cases), indent=2))