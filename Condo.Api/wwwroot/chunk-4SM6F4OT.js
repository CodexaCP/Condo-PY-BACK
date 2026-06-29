import{a as Ze}from"./chunk-CGWLERSV.js";import{a as Fe}from"./chunk-U7U42RFF.js";import"./chunk-JQBCZZYX.js";import{a as Ue}from"./chunk-COTX3Y6T.js";import{a as qe}from"./chunk-IKNNHRNW.js";import{a as $e}from"./chunk-QFMBBYS2.js";import{a as Le}from"./chunk-RHVF33ES.js";import"./chunk-VP6LVJQE.js";import{a as pe,b as Qe}from"./chunk-CPZ7YTX6.js";import"./chunk-BZ22S6RY.js";import{a as De}from"./chunk-LQEBGFMS.js";import{a as Ge,b as je}from"./chunk-YY5UQJ46.js";import"./chunk-4YFFB75L.js";import{a as Ae}from"./chunk-H55WB7EP.js";import{a as de,c as ze,d as Oe,e as Re,h as Ne,r as He}from"./chunk-FONRXXQ6.js";import{e as We}from"./chunk-A6ED6P4C.js";import{I as Pe,N as le,Q as ce,S as B,T as z,U as O}from"./chunk-XOEMQACY.js";import{C as se,D as A,E as W,f as Me,g as Be,z as Ve}from"./chunk-6ILJPY3J.js";import{e as Se,k as Ee}from"./chunk-GFQGAQTU.js";import"./chunk-6AJLGYFZ.js";import{$ as L,$a as o,Cb as N,Db as H,Eb as $,Gb as te,I as U,Ia as I,Ib as ne,J as G,Ja as K,K as j,Kb as Ce,La as Y,M as Z,Ma as E,Na as h,Nb as ve,O as C,Qb as ie,T as b,Ta as y,Tb as F,U as _,V as v,W as me,Wa as fe,Wb as M,Xa as ge,Xb as Q,_a as r,ab as a,bb as m,cb as be,da as he,db as _e,eb as D,fb as P,gb as V,gc as Te,ha as T,hb as X,ib as S,ic as oe,jb as ye,jc as Ie,kb as x,kc as re,lb as p,mc as ae,ob as J,pb as ee,qb as w,rb as k,sb as we,ua as l,vb as R,wb as f,xb as u,yb as ke,z as xe}from"./chunk-I63UPHI4.js";var dt=["data-p-icon","minus"],Ke=(()=>{class t extends O{static \u0275fac=(()=>{let e;return function(n){return(e||(e=T(t)))(n||t)}})();static \u0275cmp=I({type:t,selectors:[["","data-p-icon","minus"]],features:[E],attrs:dt,decls:1,vars:0,consts:[["d","M13.2222 7.77778H0.777778C0.571498 7.77778 0.373667 7.69584 0.227806 7.54998C0.0819442 7.40412 0 7.20629 0 7.00001C0 6.79373 0.0819442 6.5959 0.227806 6.45003C0.373667 6.30417 0.571498 6.22223 0.777778 6.22223H13.2222C13.4285 6.22223 13.6263 6.30417 13.7722 6.45003C13.9181 6.5959 14 6.79373 14 7.00001C14 7.20629 13.9181 7.40412 13.7722 7.54998C13.6263 7.69584 13.4285 7.77778 13.2222 7.77778Z","fill","currentColor"]],template:function(i,n){i&1&&(v(),D(0,"path",0))},encapsulation:2})}return t})();var Ye=`
    .p-checkbox {
        position: relative;
        display: inline-flex;
        user-select: none;
        vertical-align: bottom;
        width: dt('checkbox.width');
        height: dt('checkbox.height');
    }

    .p-checkbox-input {
        cursor: pointer;
        appearance: none;
        position: absolute;
        inset-block-start: 0;
        inset-inline-start: 0;
        width: 100%;
        height: 100%;
        padding: 0;
        margin: 0;
        opacity: 0;
        z-index: 1;
        outline: 0 none;
        border: 1px solid transparent;
        border-radius: dt('checkbox.border.radius');
    }

    .p-checkbox-box {
        display: flex;
        justify-content: center;
        align-items: center;
        border-radius: dt('checkbox.border.radius');
        border: 1px solid dt('checkbox.border.color');
        background: dt('checkbox.background');
        width: dt('checkbox.width');
        height: dt('checkbox.height');
        transition:
            background dt('checkbox.transition.duration'),
            color dt('checkbox.transition.duration'),
            border-color dt('checkbox.transition.duration'),
            box-shadow dt('checkbox.transition.duration'),
            outline-color dt('checkbox.transition.duration');
        outline-color: transparent;
        box-shadow: dt('checkbox.shadow');
    }

    .p-checkbox-icon {
        transition-duration: dt('checkbox.transition.duration');
        color: dt('checkbox.icon.color');
        font-size: dt('checkbox.icon.size');
        width: dt('checkbox.icon.size');
        height: dt('checkbox.icon.size');
    }

    .p-checkbox:not(.p-disabled):has(.p-checkbox-input:hover) .p-checkbox-box {
        border-color: dt('checkbox.hover.border.color');
    }

    .p-checkbox-checked .p-checkbox-box {
        border-color: dt('checkbox.checked.border.color');
        background: dt('checkbox.checked.background');
    }

    .p-checkbox-checked .p-checkbox-icon {
        color: dt('checkbox.icon.checked.color');
    }

    .p-checkbox-checked:not(.p-disabled):has(.p-checkbox-input:hover) .p-checkbox-box {
        background: dt('checkbox.checked.hover.background');
        border-color: dt('checkbox.checked.hover.border.color');
    }

    .p-checkbox-checked:not(.p-disabled):has(.p-checkbox-input:hover) .p-checkbox-icon {
        color: dt('checkbox.icon.checked.hover.color');
    }

    .p-checkbox:not(.p-disabled):has(.p-checkbox-input:focus-visible) .p-checkbox-box {
        border-color: dt('checkbox.focus.border.color');
        box-shadow: dt('checkbox.focus.ring.shadow');
        outline: dt('checkbox.focus.ring.width') dt('checkbox.focus.ring.style') dt('checkbox.focus.ring.color');
        outline-offset: dt('checkbox.focus.ring.offset');
    }

    .p-checkbox-checked:not(.p-disabled):has(.p-checkbox-input:focus-visible) .p-checkbox-box {
        border-color: dt('checkbox.checked.focus.border.color');
    }

    .p-checkbox.p-invalid > .p-checkbox-box {
        border-color: dt('checkbox.invalid.border.color');
    }

    .p-checkbox.p-variant-filled .p-checkbox-box {
        background: dt('checkbox.filled.background');
    }

    .p-checkbox-checked.p-variant-filled .p-checkbox-box {
        background: dt('checkbox.checked.background');
    }

    .p-checkbox-checked.p-variant-filled:not(.p-disabled):has(.p-checkbox-input:hover) .p-checkbox-box {
        background: dt('checkbox.checked.hover.background');
    }

    .p-checkbox.p-disabled {
        opacity: 1;
    }

    .p-checkbox.p-disabled .p-checkbox-box {
        background: dt('checkbox.disabled.background');
        border-color: dt('checkbox.checked.disabled.border.color');
    }

    .p-checkbox.p-disabled .p-checkbox-box .p-checkbox-icon {
        color: dt('checkbox.icon.disabled.color');
    }

    .p-checkbox-sm,
    .p-checkbox-sm .p-checkbox-box {
        width: dt('checkbox.sm.width');
        height: dt('checkbox.sm.height');
    }

    .p-checkbox-sm .p-checkbox-icon {
        font-size: dt('checkbox.icon.sm.size');
        width: dt('checkbox.icon.sm.size');
        height: dt('checkbox.icon.sm.size');
    }

    .p-checkbox-lg,
    .p-checkbox-lg .p-checkbox-box {
        width: dt('checkbox.lg.width');
        height: dt('checkbox.lg.height');
    }

    .p-checkbox-lg .p-checkbox-icon {
        font-size: dt('checkbox.icon.lg.size');
        width: dt('checkbox.icon.lg.size');
        height: dt('checkbox.icon.lg.size');
    }
`;var mt=["icon"],ht=["input"],ft=(t,c,e)=>({checked:t,class:c,dataP:e});function gt(t,c){if(t&1&&m(0,"span",8),t&2){let e=p(3);f(e.cx("icon")),r("ngClass",e.checkboxIcon)("pBind",e.ptm("icon")),y("data-p",e.dataP)}}function bt(t,c){if(t&1&&(v(),m(0,"svg",9)),t&2){let e=p(3);f(e.cx("icon")),r("pBind",e.ptm("icon")),y("data-p",e.dataP)}}function _t(t,c){if(t&1&&(P(0),h(1,gt,1,5,"span",6)(2,bt,1,4,"svg",7),V()),t&2){let e=p(2);l(),r("ngIf",e.checkboxIcon),l(),r("ngIf",!e.checkboxIcon)}}function vt(t,c){if(t&1&&(v(),m(0,"svg",10)),t&2){let e=p(2);f(e.cx("icon")),r("pBind",e.ptm("icon")),y("data-p",e.dataP)}}function xt(t,c){if(t&1&&(P(0),h(1,_t,3,2,"ng-container",3)(2,vt,1,4,"svg",5),V()),t&2){let e=p();l(),r("ngIf",e.checked),l(),r("ngIf",e._indeterminate())}}function yt(t,c){}function wt(t,c){t&1&&h(0,yt,0,0,"ng-template")}var kt=`
    ${Ye}

    /* For PrimeNG */
    p-checkBox.ng-invalid.ng-dirty .p-checkbox-box,
    p-check-box.ng-invalid.ng-dirty .p-checkbox-box,
    p-checkbox.ng-invalid.ng-dirty .p-checkbox-box {
        border-color: dt('checkbox.invalid.border.color');
    }
`,Ct={root:({instance:t})=>["p-checkbox p-component",{"p-checkbox-checked p-highlight":t.checked,"p-disabled":t.$disabled(),"p-invalid":t.invalid(),"p-variant-filled":t.$variant()==="filled","p-checkbox-sm p-inputfield-sm":t.size()==="small","p-checkbox-lg p-inputfield-lg":t.size()==="large"}],box:"p-checkbox-box",input:"p-checkbox-input",icon:"p-checkbox-icon"},Xe=(()=>{class t extends le{name="checkbox";style=kt;classes=Ct;static \u0275fac=(()=>{let e;return function(n){return(e||(e=T(t)))(n||t)}})();static \u0275prov=G({token:t,factory:t.\u0275fac})}return t})();var Je=new Z("CHECKBOX_INSTANCE"),Tt={provide:de,useExisting:U(()=>et),multi:!0},et=(()=>{class t extends $e{hostName="";value;binary;ariaLabelledBy;ariaLabel;tabindex;inputId;inputStyle;styleClass;inputClass;indeterminate=!1;formControl;checkboxIcon;readonly;autofocus;trueValue=!0;falseValue=!1;variant=F();size=F();onChange=new L;onFocus=new L;onBlur=new L;inputViewChild;get checked(){return this._indeterminate()?!1:this.binary?this.modelValue()===this.trueValue:Be(this.value,this.modelValue())}_indeterminate=he(void 0);checkboxIconTemplate;templates;_checkboxIconTemplate;focused=!1;_componentStyle=C(Xe);bindDirectiveInstance=C(B,{self:!0});$pcCheckbox=C(Je,{optional:!0,skipSelf:!0})??void 0;$variant=ie(()=>this.variant()||this.config.inputStyle()||this.config.inputVariant());onAfterContentInit(){this.templates?.forEach(e=>{switch(e.getType()){case"icon":this._checkboxIconTemplate=e.template;break;case"checkboxicon":this._checkboxIconTemplate=e.template;break}})}onChanges(e){e.indeterminate&&this._indeterminate.set(e.indeterminate.currentValue)}onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptms(["host","root"]))}updateModel(e){let i,n=this.injector.get(Oe,null,{optional:!0,self:!0}),s=n&&!this.formControl?n.value:this.modelValue();this.binary?(i=this._indeterminate()?this.trueValue:this.checked?this.falseValue:this.trueValue,this.writeModelValue(i),this.onModelChange(i)):(this.checked||this._indeterminate()?i=s.filter(d=>!Me(d,this.value)):i=s?[...s,this.value]:[this.value],this.onModelChange(i),this.writeModelValue(i),this.formControl&&this.formControl.setValue(i)),this._indeterminate()&&this._indeterminate.set(!1),this.onChange.emit({checked:i,originalEvent:e})}handleChange(e){this.readonly||this.updateModel(e)}onInputFocus(e){this.focused=!0,this.onFocus.emit(e)}onInputBlur(e){this.focused=!1,this.onBlur.emit(e),this.onModelTouched()}focus(){this.inputViewChild?.nativeElement.focus()}writeControlValue(e,i){i(e),this.cd.markForCheck()}get dataP(){return this.cn({invalid:this.invalid(),checked:this.checked,disabled:this.$disabled(),filled:this.$variant()==="filled",[this.size()]:this.size()})}static \u0275fac=(()=>{let e;return function(n){return(e||(e=T(t)))(n||t)}})();static \u0275cmp=I({type:t,selectors:[["p-checkbox"],["p-checkBox"],["p-check-box"]],contentQueries:function(i,n,s){if(i&1&&J(s,mt,4)(s,se,4),i&2){let d;w(d=k())&&(n.checkboxIconTemplate=d.first),w(d=k())&&(n.templates=d)}},viewQuery:function(i,n){if(i&1&&ee(ht,5),i&2){let s;w(s=k())&&(n.inputViewChild=s.first)}},hostVars:6,hostBindings:function(i,n){i&2&&(y("data-p-highlight",n.checked)("data-p-checked",n.checked)("data-p-disabled",n.$disabled())("data-p",n.dataP),f(n.cn(n.cx("root"),n.styleClass)))},inputs:{hostName:"hostName",value:"value",binary:[2,"binary","binary",M],ariaLabelledBy:"ariaLabelledBy",ariaLabel:"ariaLabel",tabindex:[2,"tabindex","tabindex",Q],inputId:"inputId",inputStyle:"inputStyle",styleClass:"styleClass",inputClass:"inputClass",indeterminate:[2,"indeterminate","indeterminate",M],formControl:"formControl",checkboxIcon:"checkboxIcon",readonly:[2,"readonly","readonly",M],autofocus:[2,"autofocus","autofocus",M],trueValue:"trueValue",falseValue:"falseValue",variant:[1,"variant"],size:[1,"size"]},outputs:{onChange:"onChange",onFocus:"onFocus",onBlur:"onBlur"},features:[te([Tt,Xe,{provide:Je,useExisting:t},{provide:ce,useExisting:t}]),Y([B]),E],decls:5,vars:26,consts:[["input",""],["type","checkbox",3,"focus","blur","change","checked","pBind"],[3,"pBind"],[4,"ngIf"],[4,"ngTemplateOutlet","ngTemplateOutletContext"],["data-p-icon","minus",3,"class","pBind",4,"ngIf"],[3,"class","ngClass","pBind",4,"ngIf"],["data-p-icon","check",3,"class","pBind",4,"ngIf"],[3,"ngClass","pBind"],["data-p-icon","check",3,"pBind"],["data-p-icon","minus",3,"pBind"]],template:function(i,n){if(i&1){let s=S();o(0,"input",1,0),x("focus",function(g){return b(s),_(n.onInputFocus(g))})("blur",function(g){return b(s),_(n.onInputBlur(g))})("change",function(g){return b(s),_(n.handleChange(g))}),a(),o(2,"div",2),h(3,xt,3,2,"ng-container",3)(4,wt,1,0,null,4),a()}i&2&&(R(n.inputStyle),f(n.cn(n.cx("input"),n.inputClass)),r("checked",n.checked)("pBind",n.ptm("input")),y("id",n.inputId)("value",n.value)("name",n.name())("tabindex",n.tabindex)("required",n.required()?"":void 0)("readonly",n.readonly?"":void 0)("disabled",n.$disabled()?"":void 0)("aria-labelledby",n.ariaLabelledBy)("aria-label",n.ariaLabel),l(2),f(n.cx("box")),r("pBind",n.ptm("box")),y("data-p",n.dataP),l(),r("ngIf",!n.checkboxIconTemplate&&!n._checkboxIconTemplate),l(),r("ngTemplateOutlet",n.checkboxIconTemplate||n._checkboxIconTemplate)("ngTemplateOutletContext",Ce(22,ft,n.checked,n.cx("icon"),n.dataP)))},dependencies:[ae,Te,oe,re,A,Le,Ke,z,B],encapsulation:2,changeDetection:0})}return t})(),tt=(()=>{class t{static \u0275fac=function(i){return new(i||t)};static \u0275mod=K({type:t});static \u0275inj=j({imports:[et,A,A]})}return t})();var It=["data-p-icon","eye"],nt=(()=>{class t extends O{static \u0275fac=(()=>{let e;return function(n){return(e||(e=T(t)))(n||t)}})();static \u0275cmp=I({type:t,selectors:[["","data-p-icon","eye"]],features:[E],attrs:It,decls:1,vars:0,consts:[["fill-rule","evenodd","clip-rule","evenodd","d","M0.0535499 7.25213C0.208567 7.59162 2.40413 12.4 7 12.4C11.5959 12.4 13.7914 7.59162 13.9465 7.25213C13.9487 7.2471 13.9506 7.24304 13.952 7.24001C13.9837 7.16396 14 7.08239 14 7.00001C14 6.91762 13.9837 6.83605 13.952 6.76001C13.9506 6.75697 13.9487 6.75292 13.9465 6.74788C13.7914 6.4084 11.5959 1.60001 7 1.60001C2.40413 1.60001 0.208567 6.40839 0.0535499 6.74788C0.0512519 6.75292 0.0494023 6.75697 0.048 6.76001C0.0163137 6.83605 0 6.91762 0 7.00001C0 7.08239 0.0163137 7.16396 0.048 7.24001C0.0494023 7.24304 0.0512519 7.2471 0.0535499 7.25213ZM7 11.2C3.664 11.2 1.736 7.92001 1.264 7.00001C1.736 6.08001 3.664 2.80001 7 2.80001C10.336 2.80001 12.264 6.08001 12.736 7.00001C12.264 7.92001 10.336 11.2 7 11.2ZM5.55551 9.16182C5.98308 9.44751 6.48576 9.6 7 9.6C7.68891 9.59789 8.349 9.32328 8.83614 8.83614C9.32328 8.349 9.59789 7.68891 9.59999 7C9.59999 6.48576 9.44751 5.98308 9.16182 5.55551C8.87612 5.12794 8.47006 4.7947 7.99497 4.59791C7.51988 4.40112 6.99711 4.34963 6.49276 4.44995C5.98841 4.55027 5.52513 4.7979 5.16152 5.16152C4.7979 5.52513 4.55027 5.98841 4.44995 6.49276C4.34963 6.99711 4.40112 7.51988 4.59791 7.99497C4.7947 8.47006 5.12794 8.87612 5.55551 9.16182ZM6.2222 5.83594C6.45243 5.6821 6.7231 5.6 7 5.6C7.37065 5.6021 7.72553 5.75027 7.98762 6.01237C8.24972 6.27446 8.39789 6.62934 8.4 7C8.4 7.27689 8.31789 7.54756 8.16405 7.77779C8.01022 8.00802 7.79157 8.18746 7.53575 8.29343C7.27994 8.39939 6.99844 8.42711 6.72687 8.37309C6.4553 8.31908 6.20584 8.18574 6.01005 7.98994C5.81425 7.79415 5.68091 7.54469 5.6269 7.27312C5.57288 7.00155 5.6006 6.72006 5.70656 6.46424C5.81253 6.20842 5.99197 5.98977 6.2222 5.83594Z","fill","currentColor"]],template:function(i,n){i&1&&(v(),D(0,"path",0))},encapsulation:2})}return t})();var St=["data-p-icon","eyeslash"],it=(()=>{class t extends O{pathId;onInit(){this.pathId="url(#"+Pe()+")"}static \u0275fac=(()=>{let e;return function(n){return(e||(e=T(t)))(n||t)}})();static \u0275cmp=I({type:t,selectors:[["","data-p-icon","eyeslash"]],features:[E],attrs:St,decls:5,vars:2,consts:[["fill-rule","evenodd","clip-rule","evenodd","d","M13.9414 6.74792C13.9437 6.75295 13.9455 6.757 13.9469 6.76003C13.982 6.8394 14.0001 6.9252 14.0001 7.01195C14.0001 7.0987 13.982 7.1845 13.9469 7.26386C13.6004 8.00059 13.1711 8.69549 12.6674 9.33515C12.6115 9.4071 12.54 9.46538 12.4582 9.50556C12.3765 9.54574 12.2866 9.56678 12.1955 9.56707C12.0834 9.56671 11.9737 9.53496 11.8788 9.47541C11.7838 9.41586 11.7074 9.3309 11.6583 9.23015C11.6092 9.12941 11.5893 9.01691 11.6008 8.90543C11.6124 8.79394 11.6549 8.68793 11.7237 8.5994C12.1065 8.09726 12.4437 7.56199 12.7313 6.99995C12.2595 6.08027 10.3402 2.8014 6.99732 2.8014C6.63723 2.80218 6.27816 2.83969 5.92569 2.91336C5.77666 2.93304 5.62568 2.89606 5.50263 2.80972C5.37958 2.72337 5.29344 2.59398 5.26125 2.44714C5.22907 2.30031 5.2532 2.14674 5.32885 2.01685C5.40451 1.88696 5.52618 1.79021 5.66978 1.74576C6.10574 1.64961 6.55089 1.60134 6.99732 1.60181C11.5916 1.60181 13.7864 6.40856 13.9414 6.74792ZM2.20333 1.61685C2.35871 1.61411 2.5091 1.67179 2.6228 1.77774L12.2195 11.3744C12.3318 11.4869 12.3949 11.6393 12.3949 11.7983C12.3949 11.9572 12.3318 12.1097 12.2195 12.2221C12.107 12.3345 11.9546 12.3976 11.7956 12.3976C11.6367 12.3976 11.4842 12.3345 11.3718 12.2221L10.5081 11.3584C9.46549 12.0426 8.24432 12.4042 6.99729 12.3981C2.403 12.3981 0.208197 7.59135 0.0532336 7.25198C0.0509364 7.24694 0.0490875 7.2429 0.0476856 7.23986C0.0162332 7.16518 3.05176e-05 7.08497 3.05176e-05 7.00394C3.05176e-05 6.92291 0.0162332 6.8427 0.0476856 6.76802C0.631261 5.47831 1.46902 4.31959 2.51084 3.36119L1.77509 2.62545C1.66914 2.51175 1.61146 2.36136 1.61421 2.20597C1.61695 2.05059 1.6799 1.90233 1.78979 1.79244C1.89968 1.68254 2.04794 1.6196 2.20333 1.61685ZM7.45314 8.35147L5.68574 6.57609V6.5361C5.5872 6.78938 5.56498 7.06597 5.62183 7.33173C5.67868 7.59749 5.8121 7.84078 6.00563 8.03158C6.19567 8.21043 6.43052 8.33458 6.68533 8.39089C6.94014 8.44721 7.20543 8.43359 7.45314 8.35147ZM1.26327 6.99994C1.7351 7.91163 3.64645 11.1985 6.99729 11.1985C7.9267 11.2048 8.8408 10.9618 9.64438 10.4947L8.35682 9.20718C7.86027 9.51441 7.27449 9.64491 6.69448 9.57752C6.11446 9.51014 5.57421 9.24881 5.16131 8.83592C4.74842 8.42303 4.4871 7.88277 4.41971 7.30276C4.35232 6.72274 4.48282 6.13697 4.79005 5.64041L3.35855 4.2089C2.4954 5.00336 1.78523 5.94935 1.26327 6.99994Z","fill","currentColor"],[3,"id"],["width","14","height","14","fill","white"]],template:function(i,n){i&1&&(v(),be(0,"g"),D(1,"path",0),_e(),be(2,"defs")(3,"clipPath",1),D(4,"rect",2),_e()()),i&2&&(y("clip-path",n.pathId),l(3),ye("id",n.pathId))},encapsulation:2})}return t})();var ot=`
    .p-password {
        display: inline-flex;
        position: relative;
    }

    .p-password .p-password-overlay {
        min-width: 100%;
    }

    .p-password-meter {
        height: dt('password.meter.height');
        background: dt('password.meter.background');
        border-radius: dt('password.meter.border.radius');
    }

    .p-password-meter-label {
        height: 100%;
        width: 0;
        transition: width 1s ease-in-out;
        border-radius: dt('password.meter.border.radius');
    }

    .p-password-meter-weak {
        background: dt('password.strength.weak.background');
    }

    .p-password-meter-medium {
        background: dt('password.strength.medium.background');
    }

    .p-password-meter-strong {
        background: dt('password.strength.strong.background');
    }

    .p-password-fluid {
        display: flex;
    }

    .p-password-fluid .p-password-input {
        width: 100%;
    }

    .p-password-input::-ms-reveal,
    .p-password-input::-ms-clear {
        display: none;
    }

    .p-password-overlay {
        padding: dt('password.overlay.padding');
        background: dt('password.overlay.background');
        color: dt('password.overlay.color');
        border: 1px solid dt('password.overlay.border.color');
        box-shadow: dt('password.overlay.shadow');
        border-radius: dt('password.overlay.border.radius');
    }

    .p-password-content {
        display: flex;
        flex-direction: column;
        gap: dt('password.content.gap');
    }

    .p-password-toggle-mask-icon {
        inset-inline-end: dt('form.field.padding.x');
        color: dt('password.icon.color');
        position: absolute;
        top: 50%;
        margin-top: calc(-1 * calc(dt('icon.size') / 2));
        width: dt('icon.size');
        height: dt('icon.size');
    }

    .p-password-clear-icon {
        position: absolute;
        top: 50%;
        margin-top: -0.5rem;
        cursor: pointer;
        inset-inline-end: dt('form.field.padding.x');
        color: dt('form.field.icon.color');
    }

    .p-password:has(.p-password-toggle-mask-icon) .p-password-input {
        padding-inline-end: calc((dt('form.field.padding.x') * 2) + dt('icon.size'));
    }

    .p-password:has(.p-password-toggle-mask-icon) .p-password-clear-icon {
        inset-inline-end: calc((dt('form.field.padding.x') * 2) + dt('icon.size'));
    }

    .p-password:has(.p-password-clear-icon) .p-password-input {
        padding-inline-end: calc((dt('form.field.padding.x') * 2) + dt('icon.size'));
    }

    .p-password:has(.p-password-clear-icon):has(.p-password-toggle-mask-icon)  .p-password-input {
        padding-inline-end: calc((dt('form.field.padding.x') * 3) + calc(dt('icon.size') * 2));
    }

`;var Et=["content"],Mt=["footer"],Bt=["header"],Pt=["clearicon"],Vt=["hideicon"],Lt=["showicon"],At=["overlay"],Dt=["input"],st=t=>({class:t}),Ft=t=>({width:t});function zt(t,c){if(t&1){let e=S();v(),o(0,"svg",10),x("click",function(){b(e);let n=p(2);return _(n.clear())}),a()}if(t&2){let e=p(2);f(e.cx("clearIcon")),r("pBind",e.ptm("clearIcon"))}}function Ot(t,c){}function Rt(t,c){t&1&&h(0,Ot,0,0,"ng-template")}function Nt(t,c){if(t&1){let e=S();P(0),h(1,zt,1,3,"svg",7),o(2,"span",8),x("click",function(){b(e);let n=p();return _(n.clear())}),h(3,Rt,1,0,null,9),a(),V()}if(t&2){let e=p();l(),r("ngIf",!e.clearIconTemplate&&!e._clearIconTemplate),l(),f(e.cx("clearIcon")),r("pBind",e.ptm("clearIcon")),l(),r("ngTemplateOutlet",e.clearIconTemplate||e._clearIconTemplate)}}function Ht(t,c){if(t&1){let e=S();v(),o(0,"svg",13),x("click",function(){b(e);let n=p(3);return _(n.onMaskToggle())}),a()}if(t&2){let e=p(3);f(e.cx("maskIcon")),r("pBind",e.ptm("maskIcon"))}}function $t(t,c){}function Qt(t,c){t&1&&h(0,$t,0,0,"ng-template")}function Wt(t,c){if(t&1){let e=S();o(0,"span",8),x("click",function(){b(e);let n=p(3);return _(n.onMaskToggle())}),h(1,Qt,1,0,null,14),a()}if(t&2){let e=p(3);r("pBind",e.ptm("maskIcon")),l(),r("ngTemplateOutlet",e.hideIconTemplate||e._hideIconTemplate)("ngTemplateOutletContext",ne(3,st,e.cx("maskIcon")))}}function qt(t,c){if(t&1&&(P(0),h(1,Ht,1,3,"svg",11)(2,Wt,2,5,"span",12),V()),t&2){let e=p(2);l(),r("ngIf",!e.hideIconTemplate&&!e._hideIconTemplate),l(),r("ngIf",e.hideIconTemplate||e._hideIconTemplate)}}function Ut(t,c){if(t&1){let e=S();v(),o(0,"svg",16),x("click",function(){b(e);let n=p(3);return _(n.onMaskToggle())}),a()}if(t&2){let e=p(3);f(e.cx("unmaskIcon")),r("pBind",e.ptm("unmaskIcon"))}}function Gt(t,c){}function jt(t,c){t&1&&h(0,Gt,0,0,"ng-template")}function Zt(t,c){if(t&1){let e=S();o(0,"span",8),x("click",function(){b(e);let n=p(3);return _(n.onMaskToggle())}),h(1,jt,1,0,null,14),a()}if(t&2){let e=p(3);r("pBind",e.ptm("unmaskIcon")),l(),r("ngTemplateOutlet",e.showIconTemplate||e._showIconTemplate)("ngTemplateOutletContext",ne(3,st,e.cx("unmaskIcon")))}}function Kt(t,c){if(t&1&&(P(0),h(1,Ut,1,3,"svg",15)(2,Zt,2,5,"span",12),V()),t&2){let e=p(2);l(),r("ngIf",!e.showIconTemplate&&!e._showIconTemplate),l(),r("ngIf",e.showIconTemplate||e._showIconTemplate)}}function Yt(t,c){if(t&1&&(P(0),h(1,qt,3,2,"ng-container",5)(2,Kt,3,2,"ng-container",5),V()),t&2){let e=p();l(),r("ngIf",e.unmasked),l(),r("ngIf",!e.unmasked)}}function Xt(t,c){t&1&&X(0)}function Jt(t,c){t&1&&X(0)}function en(t,c){if(t&1&&(P(0),h(1,Jt,1,0,"ng-container",9),V()),t&2){let e=p(2);l(),r("ngTemplateOutlet",e.contentTemplate||e._contentTemplate)}}function tn(t,c){if(t&1&&(o(0,"div",18)(1,"div",18),m(2,"div",19),a(),o(3,"div",18),u(4),a()()),t&2){let e=p(2);f(e.cx("content")),r("pBind",e.ptm("content")),l(),f(e.cx("meter")),r("pBind",e.ptm("meter")),l(),f(e.cx("meterLabel")),r("ngStyle",ne(15,Ft,e.meter?e.meter.width:""))("pBind",e.ptm("meterLabel")),y("data-p",e.meterDataP),l(),f(e.cx("meterText")),r("pBind",e.ptm("meterText")),l(),ke(e.infoText)}}function nn(t,c){t&1&&X(0)}function on(t,c){if(t&1){let e=S();o(0,"div",8),x("click",function(n){b(e);let s=p();return _(s.onOverlayClick(n))}),h(1,Xt,1,0,"ng-container",9)(2,en,2,1,"ng-container",17)(3,tn,5,17,"ng-template",null,3,ve)(5,nn,1,0,"ng-container",9),a()}if(t&2){let e=we(4),i=p();R(i.sx("overlay")),f(i.cx("overlay")),r("pBind",i.ptm("overlay")),y("data-p",i.overlayDataP),l(),r("ngTemplateOutlet",i.headerTemplate||i._headerTemplate),l(),r("ngIf",i.contentTemplate||i._contentTemplate)("ngIfElse",e),l(3),r("ngTemplateOutlet",i.footerTemplate||i._footerTemplate)}}var rn=`
${ot}

/* For PrimeNG */
.p-password-overlay {
    min-width: 100%;
}

p-password.ng-invalid.ng-dirty .p-inputtext {
    border-color: dt('inputtext.invalid.border.color');
}

p-password.ng-invalid.ng-dirty .p-inputtext:enabled:focus {
    border-color: dt('inputtext.focus.border.color');
}

p-password.ng-invalid.ng-dirty .p-inputtext::placeholder {
    color: dt('inputtext.invalid.placeholder.color');
}

.p-password-fluid-directive {
    width: 100%;
}

/* Animations */
.p-password-enter {
    animation: p-animate-password-enter 300ms cubic-bezier(.19,1,.22,1);
}

.p-password-leave {
    animation: p-animate-password-leave 300ms cubic-bezier(.19,1,.22,1);
}

@keyframes p-animate-password-enter {
    from {
        opacity: 0;
        transform: scale(0.93);
    }
}

@keyframes p-animate-password-leave {
    to {
        opacity: 0;
        transform: scale(0.93);
    }
}
`,an={root:({instance:t})=>({position:t.$appendTo()==="self"?"relative":void 0}),overlay:{position:"absolute"}},sn={root:({instance:t})=>["p-password p-component p-inputwrapper",{"p-inputwrapper-filled":t.$filled(),"p-variant-filled":t.$variant()==="filled","p-inputwrapper-focus":t.focused,"p-password-fluid":t.hasFluid}],rootDirective:({instance:t})=>["p-password p-inputtext p-component p-inputwrapper",{"p-inputwrapper-filled":t.$filled(),"p-variant-filled":t.$variant()==="filled","p-password-fluid-directive":t.hasFluid}],pcInputText:"p-password-input",maskIcon:"p-password-toggle-mask-icon p-password-mask-icon",unmaskIcon:"p-password-toggle-mask-icon p-password-unmask-icon",overlay:"p-password-overlay p-component",content:"p-password-content",meter:"p-password-meter",meterLabel:({instance:t})=>`p-password-meter-label ${t.meter?"p-password-meter-"+t.meter.strength:""}`,meterText:"p-password-meter-text",clearIcon:"p-password-clear-icon"},rt=(()=>{class t extends le{name="password";style=rn;classes=sn;inlineStyles=an;static \u0275fac=(()=>{let e;return function(n){return(e||(e=T(t)))(n||t)}})();static \u0275prov=G({token:t,factory:t.\u0275fac})}return t})();var at=new Z("PASSWORD_INSTANCE");var ln={provide:de,useExisting:U(()=>ue),multi:!0},ue=(()=>{class t extends qe{bindDirectiveInstance=C(B,{self:!0});$pcPassword=C(at,{optional:!0,skipSelf:!0})??void 0;onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptms(["host","root"]))}ariaLabel;ariaLabelledBy;label;promptLabel;mediumRegex="^(((?=.*[a-z])(?=.*[A-Z]))|((?=.*[a-z])(?=.*[0-9]))|((?=.*[A-Z])(?=.*[0-9])))(?=.{6,})";strongRegex="^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.{8,})";weakLabel;mediumLabel;maxLength;strongLabel;inputId;feedback=!0;toggleMask;inputStyleClass;styleClass;inputStyle;showTransitionOptions=".12s cubic-bezier(0, 0, 0.2, 1)";hideTransitionOptions=".1s linear";autocomplete;placeholder;showClear=!1;autofocus;tabindex;appendTo=F("self");motionOptions=F(void 0);overlayOptions;onFocus=new L;onBlur=new L;onClear=new L;overlayViewChild;input;contentTemplate;footerTemplate;headerTemplate;clearIconTemplate;hideIconTemplate;showIconTemplate;templates;$appendTo=ie(()=>this.appendTo()||this.config.overlayAppendTo());_contentTemplate;_footerTemplate;_headerTemplate;_clearIconTemplate;_hideIconTemplate;_showIconTemplate;overlayVisible=!1;meter;infoText;focused=!1;unmasked=!1;mediumCheckRegExp;strongCheckRegExp;resizeListener;scrollHandler;value=null;translationSubscription;_componentStyle=C(rt);overlayService=C(Ve);onInit(){this.infoText=this.promptText(),this.mediumCheckRegExp=new RegExp(this.mediumRegex),this.strongCheckRegExp=new RegExp(this.strongRegex),this.translationSubscription=this.config.translationObserver.subscribe(()=>{this.updateUI(this.value||"")})}onAfterContentInit(){this.templates.forEach(e=>{switch(e.getType()){case"content":this._contentTemplate=e.template;break;case"header":this._headerTemplate=e.template;break;case"footer":this._footerTemplate=e.template;break;case"clearicon":this._clearIconTemplate=e.template;break;case"hideicon":this._hideIconTemplate=e.template;break;case"showicon":this._showIconTemplate=e.template;break;default:this._contentTemplate=e.template;break}})}onInput(e){this.value=e.target.value,this.onModelChange(this.value)}onInputFocus(e){this.focused=!0,this.feedback&&(this.overlayVisible=!0),this.onFocus.emit(e)}onInputBlur(e){this.focused=!1,this.feedback&&(this.overlayVisible=!1),this.onModelTouched(),this.onBlur.emit(e)}onKeyUp(e){if(this.feedback){let i=e.target.value;if(this.updateUI(i),e.code==="Escape"){this.overlayVisible&&(this.overlayVisible=!1);return}this.overlayVisible||(this.overlayVisible=!0)}}updateUI(e){let i=null,n=null;switch(this.testStrength(e)){case 1:i=this.weakText(),n={strength:"weak",width:"33.33%"};break;case 2:i=this.mediumText(),n={strength:"medium",width:"66.66%"};break;case 3:i=this.strongText(),n={strength:"strong",width:"100%"};break;default:i=this.promptText(),n=null;break}this.meter=n,this.infoText=i}onMaskToggle(){this.unmasked=!this.unmasked}onOverlayClick(e){this.overlayService.add({originalEvent:e,target:this.el.nativeElement})}testStrength(e){let i=0;return this.strongCheckRegExp?.test(e)?i=3:this.mediumCheckRegExp?.test(e)?i=2:e.length&&(i=1),i}promptText(){return this.promptLabel||this.getTranslation(W.PASSWORD_PROMPT)}weakText(){return this.weakLabel||this.getTranslation(W.WEAK)}mediumText(){return this.mediumLabel||this.getTranslation(W.MEDIUM)}strongText(){return this.strongLabel||this.getTranslation(W.STRONG)}inputType(e){return e?"text":"password"}getTranslation(e){return this.config.getTranslation(e)}clear(){this.value=null,this.onModelChange(this.value),this.writeValue(this.value),this.onClear.emit()}writeControlValue(e,i){e===void 0?this.value=null:this.value=e,this.feedback&&this.updateUI(this.value||""),i(this.value),this.cd.markForCheck()}onDestroy(){this.translationSubscription&&this.translationSubscription.unsubscribe()}get containerDataP(){return this.cn({fluid:this.hasFluid})}get meterDataP(){return this.cn({[this.meter?.strength]:this.meter?.strength})}get overlayDataP(){return this.cn({["overlay-"+this.$appendTo()]:"overlay-"+this.$appendTo()})}static \u0275fac=(()=>{let e;return function(n){return(e||(e=T(t)))(n||t)}})();static \u0275cmp=I({type:t,selectors:[["p-password"]],contentQueries:function(i,n,s){if(i&1&&J(s,Et,4)(s,Mt,4)(s,Bt,4)(s,Pt,4)(s,Vt,4)(s,Lt,4)(s,se,4),i&2){let d;w(d=k())&&(n.contentTemplate=d.first),w(d=k())&&(n.footerTemplate=d.first),w(d=k())&&(n.headerTemplate=d.first),w(d=k())&&(n.clearIconTemplate=d.first),w(d=k())&&(n.hideIconTemplate=d.first),w(d=k())&&(n.showIconTemplate=d.first),w(d=k())&&(n.templates=d)}},viewQuery:function(i,n){if(i&1&&ee(At,5)(Dt,5),i&2){let s;w(s=k())&&(n.overlayViewChild=s.first),w(s=k())&&(n.input=s.first)}},hostVars:5,hostBindings:function(i,n){i&2&&(y("data-p",n.containerDataP),R(n.sx("root")),f(n.cn(n.cx("root"),n.styleClass)))},inputs:{ariaLabel:"ariaLabel",ariaLabelledBy:"ariaLabelledBy",label:"label",promptLabel:"promptLabel",mediumRegex:"mediumRegex",strongRegex:"strongRegex",weakLabel:"weakLabel",mediumLabel:"mediumLabel",maxLength:[2,"maxLength","maxLength",Q],strongLabel:"strongLabel",inputId:"inputId",feedback:[2,"feedback","feedback",M],toggleMask:[2,"toggleMask","toggleMask",M],inputStyleClass:"inputStyleClass",styleClass:"styleClass",inputStyle:"inputStyle",showTransitionOptions:"showTransitionOptions",hideTransitionOptions:"hideTransitionOptions",autocomplete:"autocomplete",placeholder:"placeholder",showClear:[2,"showClear","showClear",M],autofocus:[2,"autofocus","autofocus",M],tabindex:[2,"tabindex","tabindex",Q],appendTo:[1,"appendTo"],motionOptions:[1,"motionOptions"],overlayOptions:"overlayOptions"},outputs:{onFocus:"onFocus",onBlur:"onBlur",onClear:"onClear"},features:[te([ln,rt,{provide:at,useExisting:t},{provide:ce,useExisting:t}]),Y([B]),E],decls:8,vars:33,consts:[["input",""],["overlay",""],["content",""],["defaultContent",""],["pInputText","",3,"input","focus","blur","keyup","pSize","ngStyle","value","variant","invalid","pAutoFocus","pt","unstyled"],[4,"ngIf"],[3,"visibleChange","hostAttrSelector","visible","options","target","appendTo","unstyled","pt","motionOptions"],["data-p-icon","times",3,"class","pBind","click",4,"ngIf"],[3,"click","pBind"],[4,"ngTemplateOutlet"],["data-p-icon","times",3,"click","pBind"],["data-p-icon","eyeslash",3,"class","pBind","click",4,"ngIf"],[3,"pBind","click",4,"ngIf"],["data-p-icon","eyeslash",3,"click","pBind"],[4,"ngTemplateOutlet","ngTemplateOutletContext"],["data-p-icon","eye",3,"class","pBind","click",4,"ngIf"],["data-p-icon","eye",3,"click","pBind"],[4,"ngIf","ngIfElse"],[3,"pBind"],[3,"ngStyle","pBind"]],template:function(i,n){if(i&1){let s=S();o(0,"input",4,0),x("input",function(g){return b(s),_(n.onInput(g))})("focus",function(g){return b(s),_(n.onInputFocus(g))})("blur",function(g){return b(s),_(n.onInputBlur(g))})("keyup",function(g){return b(s),_(n.onKeyUp(g))}),a(),h(2,Nt,4,5,"ng-container",5)(3,Yt,3,2,"ng-container",5),o(4,"p-overlay",6,1),$("visibleChange",function(g){return b(s),H(n.overlayVisible,g)||(n.overlayVisible=g),_(g)}),h(6,on,6,10,"ng-template",null,2,ve),a()}i&2&&(f(n.cn(n.cx("pcInputText"),n.inputStyleClass)),r("pSize",n.size())("ngStyle",n.inputStyle)("value",n.value)("variant",n.$variant())("invalid",n.invalid())("pAutoFocus",n.autofocus)("pt",n.ptm("pcInputText"))("unstyled",n.unstyled()),y("label",n.label)("aria-label",n.ariaLabel)("aria-labelledBy",n.ariaLabelledBy)("id",n.inputId)("tabindex",n.tabindex)("type",n.unmasked?"text":"password")("placeholder",n.placeholder)("autocomplete",n.autocomplete)("name",n.name())("maxlength",n.maxlength()||n.maxLength)("minlength",n.minlength())("required",n.required()?"":void 0)("disabled",n.$disabled()?"":void 0),l(2),r("ngIf",n.showClear&&n.value!=null),l(),r("ngIf",n.toggleMask),l(),r("hostAttrSelector",n.$attrSelector),N("visible",n.overlayVisible),r("options",n.overlayOptions)("target","@parent")("appendTo",n.$appendTo())("unstyled",n.unstyled())("pt",n.ptm("pcOverlay"))("motionOptions",n.motionOptions()))},dependencies:[ae,oe,re,Ie,pe,We,Ae,it,nt,Ue,A,z,B],encapsulation:2,changeDetection:0})}return t})(),lt=(()=>{class t{static \u0275fac=function(i){return new(i||t)};static \u0275mod=K({type:t});static \u0275inj=j({imports:[ue,A,z,A,z]})}return t})();function dn(t,c){if(t&1&&m(0,"p-message",68),t&2){let e=p();r("text",e.errorMessage)}}function pn(t,c){t&1&&m(0,"i",70)}function un(t,c){t&1&&m(0,"i",71)}var ct=class t{auth=C(De);router=C(Se);email="";password="";errorMessage="";isSubmitting=!1;submit(){if(!this.email||!this.password){this.errorMessage="Ingresa tu correo o usuario, y tu contrase\xF1a.";return}this.errorMessage="",this.isSubmitting=!0,this.auth.login(this.email,this.password).pipe(xe(()=>this.isSubmitting=!1)).subscribe({next:c=>{this.router.navigateByUrl(c?"/change-password":Fe(this.auth))},error:c=>{let e=c?.error;e?.error==="duplicate_username"?this.errorMessage=e.message??"El usuario tiene cuentas en varias empresas. Usa el formato usuario@empresa para iniciar sesi\xF3n.":this.errorMessage="No se pudo iniciar sesi\xF3n. Verifica credenciales y backend."}})}static \u0275fac=function(e){return new(e||t)};static \u0275cmp=I({type:t,selectors:[["app-login"]],decls:141,vars:8,consts:[[1,"lp-root"],[1,"lp-orb","lp-orb-a"],[1,"lp-orb","lp-orb-b"],[1,"lp-orb","lp-orb-c"],[1,"lp-brand"],[1,"lp-brand-top"],[1,"lp-logo-ring"],["xmlns","http://www.w3.org/2000/svg","viewBox","0 0 200 188",1,"lp-logo-svg"],["x","8","y","10","width","76","height","138","fill","#fff","opacity",".95"],["x","16","y","20","width","14","height","21","rx","2","fill","#1AB7AF"],["x","36","y","20","width","14","height","21","rx","2","fill","#1AB7AF"],["x","56","y","20","width","14","height","21","rx","2","fill","#1AB7AF"],["x","16","y","48","width","14","height","21","rx","2","fill","#1AB7AF"],["x","36","y","48","width","14","height","21","rx","2","fill","#1AB7AF"],["x","56","y","48","width","14","height","21","rx","2","fill","#1AB7AF"],["x","16","y","76","width","14","height","21","rx","2","fill","#1AB7AF"],["x","36","y","76","width","14","height","21","rx","2","fill","#1AB7AF"],["x","56","y","76","width","14","height","21","rx","2","fill","#1AB7AF"],["x","16","y","104","width","14","height","21","rx","2","fill","#1AB7AF"],["x","36","y","104","width","14","height","21","rx","2","fill","#1AB7AF"],["x","56","y","104","width","14","height","21","rx","2","fill","#1AB7AF"],["x","4","y","148","width","82","height","8","fill","#fff","opacity",".7"],["x","90","y","24","width","60","height","124","fill","#fff","opacity",".95"],["x","98","y","34","width","12","height","17","rx","2","fill","#6AC64A"],["x","115","y","34","width","12","height","17","rx","2","fill","#6AC64A"],["x","98","y","57","width","12","height","17","rx","2","fill","#6AC64A"],["x","115","y","57","width","12","height","17","rx","2","fill","#6AC64A"],["x","98","y","80","width","12","height","17","rx","2","fill","#6AC64A"],["x","115","y","80","width","12","height","17","rx","2","fill","#6AC64A"],["x","98","y","103","width","12","height","17","rx","2","fill","#6AC64A"],["x","115","y","103","width","12","height","17","rx","2","fill","#6AC64A"],["x","148","y","94","width","44","height","54","fill","#fff","opacity",".95"],["x","156","y","104","width","12","height","15","rx","2","fill","#6AC64A"],["x","88","y","148","width","106","height","8","fill","#fff","opacity",".7"],[1,"lp-brand-name"],[1,"lp-slogan-block"],[1,"lp-slogan-eyebrow"],[1,"lp-slogan"],[1,"lp-slogan-accent"],[1,"lp-slogan-sub"],[1,"lp-divider"],[1,"lp-pillars"],[1,"lp-pillar"],[1,"lp-pillar-icon"],[1,"pi","pi-home"],[1,"pi","pi-dollar"],[1,"pi","pi-bell"],[1,"pi","pi-check-square"],[1,"lp-trust-strip"],[1,"lp-trust-item"],[1,"pi","pi-shield"],[1,"lp-trust-sep"],[1,"pi","pi-eye-slash"],[1,"pi","pi-lock"],[1,"lp-brand-footer"],[1,"lp-version-badge"],[1,"lp-form-side"],[1,"lp-form-card"],[1,"lp-mobile-logo"],[1,"lp-logo-ring","lp-logo-ring-sm"],["xmlns","http://www.w3.org/2000/svg","viewBox","0 0 200 188",1,"lp-logo-svg-sm"],[1,"lp-mobile-brand-name"],[1,"lp-form-head"],[1,"lp-field"],["for","lp-email",1,"lp-label"],["pInputText","","id","lp-email","type","text","placeholder","correo@empresa.com",1,"lp-input",3,"ngModelChange","keyup.enter","ngModel"],["for","lp-pass",1,"lp-label"],["id","lp-pass","placeholder","\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022","styleClass","lp-password",3,"ngModelChange","keyup.enter","ngModel","toggleMask","fluid","feedback"],["severity","error","styleClass","w-full",3,"text"],[1,"lp-submit-btn",3,"click","disabled"],[1,"pi","pi-spin","pi-spinner"],[1,"pi","pi-sign-in"],[1,"lp-form-footer"]],template:function(e,i){e&1&&(m(0,"app-floating-configurator"),o(1,"div",0),m(2,"div",1)(3,"div",2)(4,"div",3),o(5,"div",4)(6,"div",5)(7,"div",6),v(),o(8,"svg",7),m(9,"rect",8)(10,"rect",9)(11,"rect",10)(12,"rect",11)(13,"rect",12)(14,"rect",13)(15,"rect",14)(16,"rect",15)(17,"rect",16)(18,"rect",17)(19,"rect",18)(20,"rect",19)(21,"rect",20)(22,"rect",21)(23,"rect",22)(24,"rect",23)(25,"rect",24)(26,"rect",25)(27,"rect",26)(28,"rect",27)(29,"rect",28)(30,"rect",29)(31,"rect",30)(32,"rect",31)(33,"rect",32)(34,"rect",33),a()(),me(),o(35,"span",34),u(36,"CONDOPY"),a()(),o(37,"div",35)(38,"div",36),u(39,"Plataforma de administraci\xF3n condominal"),a(),o(40,"h1",37),u(41," Cuando todo est\xE1 claro,"),m(42,"br"),o(43,"span",38),u(44,"todos conf\xEDan."),a()(),o(45,"p",39),u(46," CONDOPY re\xFAne la administraci\xF3n, las finanzas y la comunicaci\xF3n del edificio en una plataforma dise\xF1ada para brindar "),o(47,"strong"),u(48,"transparencia, orden y tranquilidad."),a()()(),m(49,"div",40),o(50,"div",41)(51,"div",42)(52,"div",43),m(53,"i",44),a(),o(54,"div")(55,"strong"),u(56,"Multi-edificio"),a(),o(57,"p"),u(58,"Gestiona varios condominios desde un solo panel unificado."),a()()(),o(59,"div",42)(60,"div",43),m(61,"i",45),a(),o(62,"div")(63,"strong"),u(64,"Finanzas en tiempo real"),a(),o(65,"p"),u(66,"Gastos, ingresos, expensas y morosidad siempre al d\xEDa."),a()()(),o(67,"div",42)(68,"div",43),m(69,"i",46),a(),o(70,"div")(71,"strong"),u(72,"Comunicados"),a(),o(73,"p"),u(74,"Avisos y anuncios para residentes y propietarios."),a()()(),o(75,"div",42)(76,"div",43),m(77,"i",47),a(),o(78,"div")(79,"strong"),u(80,"Votaciones formales"),a(),o(81,"p"),u(82,"Decisiones con qu\xF3rum, opciones y resultados trazables."),a()()()(),o(83,"div",48)(84,"div",49),m(85,"i",50),o(86,"span"),u(87,"Acceso seguro"),a()(),m(88,"div",51),o(89,"div",49),m(90,"i",52),o(91,"span"),u(92,"Datos privados"),a()(),m(93,"div",51),o(94,"div",49),m(95,"i",53),o(96,"span"),u(97,"Roles y permisos"),a()()(),o(98,"div",54)(99,"span",55),u(100,"Panel Administrativo \xB7 v1.0"),a()()(),o(101,"div",56)(102,"div",57)(103,"div",58)(104,"div",59),v(),o(105,"svg",60),m(106,"rect",8)(107,"rect",9)(108,"rect",10)(109,"rect",11)(110,"rect",12)(111,"rect",13)(112,"rect",14)(113,"rect",22)(114,"rect",23)(115,"rect",24)(116,"rect",31)(117,"rect",32),a()(),me(),o(118,"span",61),u(119,"CONDOPY"),a()(),o(120,"div",62)(121,"h2"),u(122,"Bienvenido"),a(),o(123,"p"),u(124,"Ingresa tus credenciales para acceder al sistema"),a()(),o(125,"div",63)(126,"label",64),u(127,"Correo o nombre de usuario"),a(),o(128,"input",65),$("ngModelChange",function(s){return H(i.email,s)||(i.email=s),s}),x("keyup.enter",function(){return i.submit()}),a()(),o(129,"div",63)(130,"label",66),u(131,"Contrase\xF1a"),a(),o(132,"p-password",67),$("ngModelChange",function(s){return H(i.password,s)||(i.password=s),s}),x("keyup.enter",function(){return i.submit()}),a()(),fe(133,dn,1,1,"p-message",68),o(134,"button",69),x("click",function(){return i.submit()}),fe(135,pn,1,0,"i",70)(136,un,1,0,"i",71),u(137," Entrar al sistema "),a(),o(138,"div",72),m(139,"i",53),u(140," Acceso seguro \xB7 CONDOPY "),a()()()()),e&2&&(l(128),N("ngModel",i.email),l(4),N("ngModel",i.password),r("toggleMask",!0)("fluid",!0)("feedback",!1),l(),ge(i.errorMessage?133:-1),l(),r("disabled",i.isSubmitting),l(),ge(i.isSubmitting?135:136))},dependencies:[tt,Qe,pe,lt,ue,He,ze,Re,Ne,Ee,je,Ge,Ze],encapsulation:2})};export{ct as Login};
