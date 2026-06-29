import{a as Z}from"./chunk-CPZ7YTX6.js";import"./chunk-BZ22S6RY.js";import{a as j}from"./chunk-LQEBGFMS.js";import{a as J}from"./chunk-2TXRLIKG.js";import{c as z,e as W,f as V,g as q,h as R,i as Y,p as G,r as H}from"./chunk-FONRXXQ6.js";import{f as U}from"./chunk-A6ED6P4C.js";import{N as T,Q as B,R as L,S as g,T as O}from"./chunk-XOEMQACY.js";import{D as I,y as A}from"./chunk-6ILJPY3J.js";import{e as N}from"./chunk-GFQGAQTU.js";import"./chunk-6AJLGYFZ.js";import{$a as l,Cb as c,Db as u,Eb as b,Gb as k,Ia as f,J as v,La as C,M as y,Ma as P,O as r,Ub as D,_a as M,ab as o,bb as S,ha as h,kb as x,mb as F,mc as m,nb as _,ua as d,wb as E,xb as s,z as w}from"./chunk-I63UPHI4.js";var K=`
    .p-floatlabel {
        display: block;
        position: relative;
    }

    .p-floatlabel label {
        position: absolute;
        pointer-events: none;
        top: 50%;
        transform: translateY(-50%);
        transition-property: all;
        transition-timing-function: ease;
        line-height: 1;
        font-weight: dt('floatlabel.font.weight');
        inset-inline-start: dt('floatlabel.position.x');
        color: dt('floatlabel.color');
        transition-duration: dt('floatlabel.transition.duration');
    }

    .p-floatlabel:has(.p-textarea) label {
        top: dt('floatlabel.position.y');
        transform: translateY(0);
    }

    .p-floatlabel:has(.p-inputicon:first-child) label {
        inset-inline-start: calc((dt('form.field.padding.x') * 2) + dt('icon.size'));
    }

    .p-floatlabel:has(input:focus) label,
    .p-floatlabel:has(input.p-filled) label,
    .p-floatlabel:has(input:-webkit-autofill) label,
    .p-floatlabel:has(textarea:focus) label,
    .p-floatlabel:has(textarea.p-filled) label,
    .p-floatlabel:has(.p-inputwrapper-focus) label,
    .p-floatlabel:has(.p-inputwrapper-filled) label,
    .p-floatlabel:has(input[placeholder]) label,
    .p-floatlabel:has(textarea[placeholder]) label {
        top: dt('floatlabel.over.active.top');
        transform: translateY(0);
        font-size: dt('floatlabel.active.font.size');
        font-weight: dt('floatlabel.active.font.weight');
    }

    .p-floatlabel:has(input.p-filled) label,
    .p-floatlabel:has(textarea.p-filled) label,
    .p-floatlabel:has(.p-inputwrapper-filled) label {
        color: dt('floatlabel.active.color');
    }

    .p-floatlabel:has(input:focus) label,
    .p-floatlabel:has(input:-webkit-autofill) label,
    .p-floatlabel:has(textarea:focus) label,
    .p-floatlabel:has(.p-inputwrapper-focus) label {
        color: dt('floatlabel.focus.color');
    }

    .p-floatlabel-in .p-inputtext,
    .p-floatlabel-in .p-textarea,
    .p-floatlabel-in .p-select-label,
    .p-floatlabel-in .p-multiselect-label,
    .p-floatlabel-in .p-multiselect-label:has(.p-chip),
    .p-floatlabel-in .p-autocomplete-input-multiple,
    .p-floatlabel-in .p-cascadeselect-label,
    .p-floatlabel-in .p-treeselect-label {
        padding-block-start: dt('floatlabel.in.input.padding.top');
        padding-block-end: dt('floatlabel.in.input.padding.bottom');
    }

    .p-floatlabel-in:has(input:focus) label,
    .p-floatlabel-in:has(input.p-filled) label,
    .p-floatlabel-in:has(input:-webkit-autofill) label,
    .p-floatlabel-in:has(textarea:focus) label,
    .p-floatlabel-in:has(textarea.p-filled) label,
    .p-floatlabel-in:has(.p-inputwrapper-focus) label,
    .p-floatlabel-in:has(.p-inputwrapper-filled) label,
    .p-floatlabel-in:has(input[placeholder]) label,
    .p-floatlabel-in:has(textarea[placeholder]) label {
        top: dt('floatlabel.in.active.top');
    }

    .p-floatlabel-on:has(input:focus) label,
    .p-floatlabel-on:has(input.p-filled) label,
    .p-floatlabel-on:has(input:-webkit-autofill) label,
    .p-floatlabel-on:has(textarea:focus) label,
    .p-floatlabel-on:has(textarea.p-filled) label,
    .p-floatlabel-on:has(.p-inputwrapper-focus) label,
    .p-floatlabel-on:has(.p-inputwrapper-filled) label,
    .p-floatlabel-on:has(input[placeholder]) label,
    .p-floatlabel-on:has(textarea[placeholder]) label {
        top: 0;
        transform: translateY(-50%);
        border-radius: dt('floatlabel.on.border.radius');
        background: dt('floatlabel.on.active.background');
        padding: dt('floatlabel.on.active.padding');
    }

    .p-floatlabel:has([class^='p-'][class$='-fluid']) {
        width: 100%;
    }

    .p-floatlabel:has(.p-invalid) label {
        color: dt('floatlabel.invalid.color');
    }
`;var te=["*"],ne=`
    ${K}

    /* For PrimeNG */
    .p-floatlabel:has(.ng-invalid.ng-dirty) label {
        color: dt('floatlabel.invalid.color');
    }
`,le={root:({instance:e})=>["p-floatlabel",{"p-floatlabel-over":e.variant==="over","p-floatlabel-on":e.variant==="on","p-floatlabel-in":e.variant==="in"}]},Q=(()=>{class e extends T{name="floatlabel";style=ne;classes=le;static \u0275fac=(()=>{let t;return function(i){return(t||(t=h(e)))(i||e)}})();static \u0275prov=v({token:e,factory:e.\u0275fac})}return e})();var X=new y("FLOATLABEL_INSTANCE"),$=(()=>{class e extends L{_componentStyle=r(Q);$pcFloatLabel=r(X,{optional:!0,skipSelf:!0})??void 0;bindDirectiveInstance=r(g,{self:!0});onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptms(["host","root"]))}variant="over";static \u0275fac=(()=>{let t;return function(i){return(t||(t=h(e)))(i||e)}})();static \u0275cmp=f({type:e,selectors:[["p-floatlabel"],["p-floatLabel"],["p-float-label"]],hostVars:2,hostBindings:function(a,i){a&2&&E(i.cx("root"))},inputs:{variant:"variant"},features:[k([Q,{provide:X,useExisting:e},{provide:B,useExisting:e}]),C([g]),P],ngContentSelectors:te,decls:1,vars:0,template:function(a,i){a&1&&(F(),_(0))},dependencies:[m,I,O],encapsulation:2,changeDetection:0})}return e})();var ee=class e{auth=r(j);router=r(N);cdr=r(D);msg=r(A);currentPassword="";newPassword="";confirmPassword="";isSubmitting=!1;submit(){if(this.newPassword!==this.confirmPassword){this.msg.add({severity:"error",summary:"Error",detail:"La confirmaci\xF3n no coincide.",life:5e3});return}if(!this.isValidPassword(this.newPassword)){this.msg.add({severity:"error",summary:"Error",detail:"La nueva contrase\xF1a no cumple la pol\xEDtica m\xEDnima.",life:5e3});return}this.isSubmitting=!0,this.auth.changePassword(this.currentPassword,this.newPassword).pipe(w(()=>this.isSubmitting=!1)).subscribe({next:()=>{this.router.navigateByUrl("/dashboard"),this.cdr.markForCheck()},error:()=>{this.msg.add({severity:"error",summary:"Error",detail:"No se pudo actualizar la contrase\xF1a.",life:5e3}),this.cdr.markForCheck()}})}isValidPassword(p){return p.length>=8&&/[A-Z]/.test(p)&&/[a-z]/.test(p)&&/[^A-Za-z0-9]/.test(p)}static \u0275fac=function(t){return new(t||e)};static \u0275cmp=f({type:e,selectors:[["app-change-password-page"]],decls:23,vars:4,consts:[[1,"password-page"],["styleClass","password-card"],[3,"ngSubmit"],["variant","on"],["pInputText","","name","currentPassword","type","password","required","",3,"ngModelChange","ngModel"],["pInputText","","name","newPassword","type","password","required","",3,"ngModelChange","ngModel"],["pInputText","","name","confirmPassword","type","password","required","",3,"ngModelChange","ngModel"],[1,"hint"],["type","submit","label","Actualizar contrase\xF1a",3,"loading"]],template:function(t,a){t&1&&(l(0,"section",0)(1,"p-card",1)(2,"form",2),x("ngSubmit",function(){return a.submit()}),l(3,"div")(4,"h1"),s(5,"Cambio obligatorio de contrase\xF1a"),o(),l(6,"p"),s(7,"Antes de continuar, define una nueva clave segura para tu cuenta."),o()(),l(8,"p-floatlabel",3)(9,"input",4),b("ngModelChange",function(n){return u(a.currentPassword,n)||(a.currentPassword=n),n}),o(),l(10,"label"),s(11,"Contrase\xF1a actual"),o()(),l(12,"p-floatlabel",3)(13,"input",5),b("ngModelChange",function(n){return u(a.newPassword,n)||(a.newPassword=n),n}),o(),l(14,"label"),s(15,"Nueva contrase\xF1a"),o()(),l(16,"p-floatlabel",3)(17,"input",6),b("ngModelChange",function(n){return u(a.confirmPassword,n)||(a.confirmPassword=n),n}),o(),l(18,"label"),s(19,"Confirmar nueva contrase\xF1a"),o()(),l(20,"p",7),s(21,"Debe tener m\xEDnimo 8 caracteres, may\xFAscula, min\xFAscula y caracter especial."),o(),S(22,"p-button",8),o()()()),t&2&&(d(9),c("ngModel",a.currentPassword),d(4),c("ngModel",a.newPassword),d(4),c("ngModel",a.confirmPassword),d(5),M("loading",a.isSubmitting))},dependencies:[m,H,Y,z,W,V,G,R,q,U,J,$,Z],styles:[".password-page[_ngcontent-%COMP%]{min-height:100vh;display:grid;place-items:center;background:radial-gradient(circle at top left,rgba(26,183,175,.22),transparent 24%),radial-gradient(circle at right center,rgba(19,133,182,.18),transparent 24%),linear-gradient(135deg,#f3fbfb,#eef9fa 48%,#f4fbf2);padding:2rem}.password-card[_ngcontent-%COMP%]{width:min(520px,100%)}[_nghost-%COMP%]     .password-card.p-card{background:#ffffffeb;border:1px solid rgba(19,133,182,.1);border-radius:28px;box-shadow:0 24px 60px #11364a24}[_nghost-%COMP%]     .password-card .p-card-body{padding:2rem}form[_ngcontent-%COMP%]{display:grid;gap:1rem}h1[_ngcontent-%COMP%]{margin:0 0 .5rem;color:var(--brand-ink)}p[_ngcontent-%COMP%]{margin:0;color:var(--brand-muted)}.hint[_ngcontent-%COMP%]{color:var(--brand-muted);font-size:.92rem}"]})};export{ee as ChangePasswordPageComponent};
