import{a as k}from"./chunk-BZ22S6RY.js";import{d as M}from"./chunk-FONRXXQ6.js";import{b as D}from"./chunk-A6ED6P4C.js";import{N as y,Q as N,S as r}from"./chunk-XOEMQACY.js";import{Gb as I,J as a,Ja as f,K as l,Ka as x,La as g,M as s,Ma as v,O as o,Qb as T,Ta as h,Tb as i,Wb as u,ea as p,ha as c,kb as b,wb as m}from"./chunk-I63UPHI4.js";var w=`
    .p-inputtext {
        font-family: inherit;
        font-feature-settings: inherit;
        font-size: 1rem;
        color: dt('inputtext.color');
        background: dt('inputtext.background');
        padding-block: dt('inputtext.padding.y');
        padding-inline: dt('inputtext.padding.x');
        border: 1px solid dt('inputtext.border.color');
        transition:
            background dt('inputtext.transition.duration'),
            color dt('inputtext.transition.duration'),
            border-color dt('inputtext.transition.duration'),
            outline-color dt('inputtext.transition.duration'),
            box-shadow dt('inputtext.transition.duration');
        appearance: none;
        border-radius: dt('inputtext.border.radius');
        outline-color: transparent;
        box-shadow: dt('inputtext.shadow');
    }

    .p-inputtext:enabled:hover {
        border-color: dt('inputtext.hover.border.color');
    }

    .p-inputtext:enabled:focus {
        border-color: dt('inputtext.focus.border.color');
        box-shadow: dt('inputtext.focus.ring.shadow');
        outline: dt('inputtext.focus.ring.width') dt('inputtext.focus.ring.style') dt('inputtext.focus.ring.color');
        outline-offset: dt('inputtext.focus.ring.offset');
    }

    .p-inputtext.p-invalid {
        border-color: dt('inputtext.invalid.border.color');
    }

    .p-inputtext.p-variant-filled {
        background: dt('inputtext.filled.background');
    }

    .p-inputtext.p-variant-filled:enabled:hover {
        background: dt('inputtext.filled.hover.background');
    }

    .p-inputtext.p-variant-filled:enabled:focus {
        background: dt('inputtext.filled.focus.background');
    }

    .p-inputtext:disabled {
        opacity: 1;
        background: dt('inputtext.disabled.background');
        color: dt('inputtext.disabled.color');
    }

    .p-inputtext::placeholder {
        color: dt('inputtext.placeholder.color');
    }

    .p-inputtext.p-invalid::placeholder {
        color: dt('inputtext.invalid.placeholder.color');
    }

    .p-inputtext-sm {
        font-size: dt('inputtext.sm.font.size');
        padding-block: dt('inputtext.sm.padding.y');
        padding-inline: dt('inputtext.sm.padding.x');
    }

    .p-inputtext-lg {
        font-size: dt('inputtext.lg.font.size');
        padding-block: dt('inputtext.lg.padding.y');
        padding-inline: dt('inputtext.lg.padding.x');
    }

    .p-inputtext-fluid {
        width: 100%;
    }
`;var P=`
    ${w}

    /* For PrimeNG */
   .p-inputtext.ng-invalid.ng-dirty {
        border-color: dt('inputtext.invalid.border.color');
    }

    .p-inputtext.ng-invalid.ng-dirty::placeholder {
        color: dt('inputtext.invalid.placeholder.color');
    }
`,z={root:({instance:t})=>["p-inputtext p-component",{"p-filled":t.$filled(),"p-inputtext-sm":t.pSize==="small","p-inputtext-lg":t.pSize==="large","p-invalid":t.invalid(),"p-variant-filled":t.$variant()==="filled","p-inputtext-fluid":t.hasFluid}]},F=(()=>{class t extends y{name="inputtext";style=P;classes=z;static \u0275fac=(()=>{let n;return function(d){return(n||(n=c(t)))(d||t)}})();static \u0275prov=a({token:t,factory:t.\u0275fac})}return t})();var S=new s("INPUTTEXT_INSTANCE"),O=(()=>{class t extends k{hostName="";ptInputText=i();pInputTextPT=i();pInputTextUnstyled=i();bindDirectiveInstance=o(r,{self:!0});$pcInputText=o(S,{optional:!0,skipSelf:!0})??void 0;ngControl=o(M,{optional:!0,self:!0});pcFluid=o(D,{optional:!0,host:!0,skipSelf:!0});pSize;variant=i();fluid=i(void 0,{transform:u});invalid=i(void 0,{transform:u});$variant=T(()=>this.variant()||this.config.inputStyle()||this.config.inputVariant());_componentStyle=o(F);constructor(){super(),p(()=>{let n=this.ptInputText()||this.pInputTextPT();n&&this.directivePT.set(n)}),p(()=>{this.pInputTextUnstyled()&&this.directiveUnstyled.set(this.pInputTextUnstyled())})}onAfterViewInit(){this.writeModelValue(this.ngControl?.value??this.el.nativeElement.value),this.cd.detectChanges()}onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptm("root"))}onDoCheck(){this.writeModelValue(this.ngControl?.value??this.el.nativeElement.value)}onInput(){this.writeModelValue(this.ngControl?.value??this.el.nativeElement.value)}get hasFluid(){return this.fluid()??!!this.pcFluid}get dataP(){return this.cn({invalid:this.invalid(),fluid:this.hasFluid,filled:this.$variant()==="filled",[this.pSize]:this.pSize})}static \u0275fac=function(e){return new(e||t)};static \u0275dir=x({type:t,selectors:[["","pInputText",""]],hostVars:3,hostBindings:function(e,d){e&1&&b("input",function(){return d.onInput()}),e&2&&(h("data-p",d.dataP),m(d.cx("root")))},inputs:{hostName:"hostName",ptInputText:[1,"ptInputText"],pInputTextPT:[1,"pInputTextPT"],pInputTextUnstyled:[1,"pInputTextUnstyled"],pSize:"pSize",variant:[1,"variant"],fluid:[1,"fluid"],invalid:[1,"invalid"]},features:[I([F,{provide:S,useExisting:t},{provide:N,useExisting:t}]),g([r]),v]})}return t})(),Q=(()=>{class t{static \u0275fac=function(e){return new(e||t)};static \u0275mod=f({type:t});static \u0275inj=l({})}return t})();export{O as a,Q as b};
